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
}

internal sealed record FileResult(string Source, string? Output, FileOutcome Outcome, string? Error = null);

internal sealed record BatchResult(TargetFormat Target, IReadOnlyList<FileResult> Files)
{
    public int Converted => Files.Count(f => f.Outcome == FileOutcome.Converted);
    public int Skipped => Files.Count(f => f.Outcome == FileOutcome.Skipped);
    public int Failed => Files.Count(f => f.Outcome == FileOutcome.Failed);
    public string? FirstOutput => Files.FirstOrDefault(f => f.Output is not null)?.Output;
}

/// <summary>원본 옆에 새 파일을 만든다. 원본은 건드리지 않는다.</summary>
internal sealed class ConversionService
{
    readonly SemaphoreSlim _slots = new(Math.Clamp(Environment.ProcessorCount / 2, 1, 3));
    readonly FfmpegRunner _ffmpeg = new();

    public bool FfmpegAvailable => _ffmpeg.ExecutablePath is not null;

    public async Task<BatchResult> ConvertAsync(IReadOnlyList<string> files, TargetFormat menuTarget)
    {
        if (menuTarget.Engine == ConversionEngine.Archive)
            return await ArchiveAsync(files, menuTarget);

        var results = await Task.WhenAll(files.Select(f => ConvertOneAsync(f, menuTarget.Id)));
        return new BatchResult(menuTarget, results);
    }

    /// <summary>압축은 파일마다가 아니라 끌어 온 것 전부를 ZIP 하나로 묶는다.</summary>
    async Task<BatchResult> ArchiveAsync(IReadOnlyList<string> items, TargetFormat target)
    {
        await _slots.WaitAsync();
        try
        {
            var output = await Archiver.CreateAsync(items);
            AppLog.Write($"압축: {items.Count}개 → {output}");
            return new BatchResult(target, items.Select(i => new FileResult(i, output, FileOutcome.Converted)).ToArray());
        }
        catch (Exception ex)
        {
            AppLog.Write($"압축 실패: {ex.Message}");
            return new BatchResult(target, items.Select(i => new FileResult(i, null, FileOutcome.Failed, ex.Message)).ToArray());
        }
        finally
        {
            _slots.Release();
        }
    }

    async Task<FileResult> ConvertOneAsync(string source, string targetId)
    {
        // 여러 종류를 같이 끌어 오면 같은 형식이라도 파일마다 변환 방법이 다를 수 있다 (GIF→MP4 와 MOV→MP4)
        var target = FormatCatalog.Resolve(source, targetId);
        if (target is null || FormatCatalog.IsSameFormat(source, target))
            return new FileResult(source, null, FileOutcome.Skipped);

        await _slots.WaitAsync();
        string? output = null;
        try
        {
            output = OutputPaths.Reserve(source, target.Extension);

            switch (target.Engine)
            {
                case ConversionEngine.Magick:
                    await Task.Run(() => ImageConverter.Convert(source, output, target.Id));
                    break;
                case ConversionEngine.Document:
                    await DocumentConverter.ConvertAsync(source, output);
                    break;
                default:
                    await _ffmpeg.RunAsync(FfmpegArguments.Build(source, output, target.Id));
                    break;
            }

            if (new FileInfo(output).Length == 0)
                throw new InvalidOperationException("결과 파일이 비어 있습니다.");

            AppLog.Write($"변환: {source} → {output}");
            return new FileResult(source, output, FileOutcome.Converted);
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
            _slots.Release();
        }
    }

    static void TryDelete(string path)
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
