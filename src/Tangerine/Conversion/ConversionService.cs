using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tangerine.Services;
using Tangerine.Core;

namespace Tangerine.Conversion;

internal enum FileOutcome
{
    Converted,
    Skipped,
    Failed,
    Canceled,
}

/// <param name="Output">만든 파일 (여러 개면 첫 번째)</param>
/// <param name="Made">만든 파일 수. PDF → 그림은 쪽 수만큼.</param>
internal sealed record FileResult(string Source, string? Output, FileOutcome Outcome, string? Error = null, int Made = 1);

internal sealed record BatchResult(TargetFormat Target, IReadOnlyList<FileResult> Files)
{
    public int Converted => Files.Count(f => f.Outcome == FileOutcome.Converted);
    public int Made => Files.Where(f => f.Outcome == FileOutcome.Converted).Sum(f => f.Made);
    public int Skipped => Files.Count(f => f.Outcome == FileOutcome.Skipped);
    public int Failed => Files.Count(f => f.Outcome == FileOutcome.Failed);
    public int Canceled => Files.Count(f => f.Outcome == FileOutcome.Canceled);
    public string? FirstOutput => Files.FirstOrDefault(f => f.Output is not null)?.Output;
}

/// <summary>원본 옆에 새 파일을 만든다. 원본은 건드리지 않는다.</summary>
internal sealed class ConversionService
{
    readonly SemaphoreSlim _slots = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 3));
    readonly FfmpegRunner _ffmpeg = new();
    readonly object _gate = new();
    CancellationTokenSource _cancel = new();

    // 진행률: 맡은 파일 수 대비 끝낸 양. 파일 하나 안의 진행은 영상·오디오·PDF 만 알린다.
    int _total;
    int _finished;
    readonly Dictionary<object, double> _running = [];

    public bool FfmpegAvailable => _ffmpeg.ExecutablePath is not null;

    /// <summary>전체 진행률 0~1. 할 일이 모두 끝나면 null. 아무 스레드에서나 불린다.</summary>
    public event Action<double?>? ProgressChanged;

    /// <summary>진행 중이거나 기다리는 변환을 모두 멈춘다. 이미 시작한 Office 문서 하나는 끝까지 한다.</summary>
    public void CancelAll()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            old = _cancel;
            _cancel = new CancellationTokenSource();
        }
        old.Cancel();
        AppLog.Write("변환 취소");
    }

    public async Task<BatchResult> ConvertAsync(IReadOnlyList<string> files, TargetFormat menuTarget)
    {
        if (menuTarget.Engine == ConversionEngine.CopyPath)
            return CopyPaths(files, menuTarget);

        CancellationToken cancel;
        lock (_gate)
        {
            cancel = _cancel.Token;
            _total += menuTarget.Engine is ConversionEngine.Archive or ConversionEngine.PdfMerge ? 1 : files.Count;
        }
        Report();

        if (menuTarget.Engine is ConversionEngine.Archive or ConversionEngine.PdfMerge)
            return await CombineAsync(files, menuTarget, cancel);

        var results = await Task.WhenAll(files.Select(f => ConvertOneAsync(f, menuTarget.Id, cancel)));
        return new BatchResult(menuTarget, results);
    }

    /// <summary>
    /// 끌어 온 것의 전체 경로를 한 줄에 하나씩 클립보드에 넣는다.
    /// 클립보드는 화면 스레드에서만 쓸 수 있어 await 전에 (메뉴에서 부른 그 자리에서) 한다.
    /// </summary>
    static BatchResult CopyPaths(IReadOnlyList<string> items, TargetFormat target)
    {
        var paths = items.Select(Path.GetFullPath).ToList();
        try
        {
            System.Windows.Clipboard.SetText(string.Join("\r\n", paths));
            AppLog.Write($"경로 복사: {paths.Count}개");
            return new BatchResult(target, paths.Select(p => new FileResult(p, null, FileOutcome.Converted)).ToArray());
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            // 다른 프로그램이 클립보드를 붙잡고 있을 때
            return new BatchResult(target, paths.Select(p => new FileResult(p, null, FileOutcome.Failed, "클립보드를 쓸 수 없습니다. 잠시 뒤 다시 해 보세요")).ToArray());
        }
    }

    /// <summary>압축과 PDF 합치기는 파일마다가 아니라 끌어 온 것 전부를 결과 하나로 묶는다.</summary>
    async Task<BatchResult> CombineAsync(IReadOnlyList<string> items, TargetFormat target, CancellationToken cancel)
    {
        var job = new object();
        var holding = false;
        try
        {
            await _slots.WaitAsync(cancel);
            holding = true;
            var output = target.Engine == ConversionEngine.PdfMerge
                ? await PdfMerger.MergeAsync(items)
                : await Archiver.CreateAsync(items);
            AppLog.Write($"{target.Label}: {items.Count}개 → {output}");
            return new BatchResult(target, items.Select(i => new FileResult(i, output, FileOutcome.Converted)).ToArray());
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            return new BatchResult(target, items.Select(i => new FileResult(i, null, FileOutcome.Canceled)).ToArray());
        }
        catch (Exception ex)
        {
            AppLog.Write($"{target.Label} 실패: {ex.Message}");
            return new BatchResult(target, items.Select(i => new FileResult(i, null, FileOutcome.Failed, ex.Message)).ToArray());
        }
        finally
        {
            if (holding)
                _slots.Release();
            Finish(job);
        }
    }

    async Task<FileResult> ConvertOneAsync(string source, string targetId, CancellationToken cancel)
    {
        var job = new object();
        // 여러 종류를 같이 끌어 오면 같은 형식이라도 파일마다 변환 방법이 다를 수 있다 (GIF→MP4 와 MOV→MP4)
        var target = FormatCatalog.Resolve(source, targetId);
        if (target is null || FormatCatalog.IsSameFormat(source, target))
        {
            Finish(job);
            return new FileResult(source, null, FileOutcome.Skipped);
        }

        var holding = false;
        string? output = null;
        void Progress(double fraction) => Update(job, fraction);
        try
        {
            await _slots.WaitAsync(cancel);
            holding = true;

            // 쪽·장·시트마다 파일을 따로 만드는 변환은 이름도 스스로 정한다
            if (target.Engine is ConversionEngine.PdfPages or ConversionEngine.PdfSplit or ConversionEngine.Slides or ConversionEngine.Spreadsheet)
            {
                var made = target.Engine switch
                {
                    ConversionEngine.PdfSplit => await PdfSplitter.SplitAsync(source),
                    ConversionEngine.PdfPages => await PdfPages.ConvertAsync(source, target.Extension, cancel, Progress),
                    ConversionEngine.Slides => await DocumentConverter.SlidesAsync(source, target.Extension, cancel),
                    _ => await DocumentConverter.SpreadsheetAsync(source, target.Id, cancel),
                };
                if (made.Count == 0)
                    return new FileResult(source, null, FileOutcome.Skipped);
                AppLog.Write($"변환: {source} → {made.Count}개 ({made[0]} …)");
                return new FileResult(source, made[0], FileOutcome.Converted, Made: made.Count);
            }

            cancel.ThrowIfCancellationRequested();
            output = OutputPaths.Reserve(source, target.Extension);

            switch (target.Engine)
            {
                case ConversionEngine.Magick:
                    await Task.Run(() => ImageConverter.Convert(source, output, target.Id));
                    break;
                case ConversionEngine.Document:
                    await DocumentConverter.ConvertAsync(source, output, cancel);
                    break;
                case ConversionEngine.Subtitle:
                    SubtitleConverter.ConvertFile(source, output, target.Id);
                    break;
                default:
                    await _ffmpeg.RunAsync(FfmpegArguments.Build(source, output, target.Id), cancel, Progress);
                    break;
            }

            if (new FileInfo(output).Length == 0)
                throw new InvalidOperationException("결과 파일이 비어 있습니다.");

            AppLog.Write($"변환: {source} → {output}");
            return new FileResult(source, output, FileOutcome.Converted);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (output is not null)
                TryDelete(output);
            return new FileResult(source, null, FileOutcome.Canceled);
        }
        catch (Exception ex)
        {
            if (output is not null)
                TryDelete(output);
            AppLog.Write($"실패: {source} → {target.Label}: {ex.Message}");
            return new FileResult(source, null, FileOutcome.Failed, ex.Message);
        }
        finally
        {
            if (holding)
                _slots.Release();
            Finish(job);
        }
    }

    void Update(object job, double fraction)
    {
        lock (_gate)
            _running[job] = fraction;
        Report();
    }

    void Finish(object job)
    {
        lock (_gate)
        {
            _running.Remove(job);
            _finished++;
        }
        Report();
    }

    void Report()
    {
        double? progress;
        lock (_gate)
        {
            if (_finished >= _total)
            {
                _total = _finished = 0;
                _running.Clear();
                progress = null;
            }
            else
            {
                progress = (_finished + _running.Values.Sum()) / _total;
            }
        }
        ProgressChanged?.Invoke(progress);
    }

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
