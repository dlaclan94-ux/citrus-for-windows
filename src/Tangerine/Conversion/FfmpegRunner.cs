using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tangerine.Services;

namespace Tangerine.Conversion;

internal sealed class FfmpegRunner
{
    /// <summary>실행 파일 옆 tools\ffmpeg.exe, 없으면 PATH 에서 찾는다. 둘 다 없으면 null.</summary>
    public string? ExecutablePath { get; } = Locate();

    /// <param name="progress">원본 길이 대비 지금까지 만든 길이 (0~1). 길이를 모르면 부르지 않는다.</param>
    public async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default, Action<double>? progress = null)
    {
        if (ExecutablePath is null)
            throw new InvalidOperationException("ffmpeg.exe 를 찾을 수 없습니다. 영상·오디오 변환에 필요합니다.");

        var input = arguments.SkipWhile(a => a != "-i").Skip(1).FirstOrDefault();
        var duration = progress is not null && input is not null ? await DurationAsync(input, cancellationToken) : 0;

        var start = Start(arguments);
        // 진행 상황을 표준 출력으로 받는다 (out_time_us=…)
        start.ArgumentList.Insert(0, "-progress");
        start.ArgumentList.Insert(1, "pipe:1");
        start.ArgumentList.Insert(2, "-nostats");

        using var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg 를 실행하지 못했습니다.");
        try
        {
            // 변환 중에도 PC 가 버벅이지 않도록
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (InvalidOperationException)
        {
            // 이미 끝난 경우
        }

        // 취소하면 ffmpeg 를 바로 끈다 (만들던 파일은 부른 쪽에서 지운다)
        using var kill = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        });

        var stderr = process.StandardError.ReadToEndAsync();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
        {
            if (duration > 0 && line.StartsWith("out_time_us=") && long.TryParse(line.AsSpan(12), out var us))
                progress!(Math.Clamp(us / 1_000_000.0 / duration, 0, 1));
        }
        await process.WaitForExitAsync();
        var error = await stderr;
        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            AppLog.Write($"ffmpeg 오류 (코드 {process.ExitCode}): {LastLines(error, 3)}");
            // 소리 없는 영상에서 '소리만' 을 고른 경우처럼 원본에 필요한 트랙이 없을 때
            throw new InvalidOperationException(error.Contains("does not contain any stream")
                ? "원본에 바꿀 소리(또는 영상)가 없습니다"
                : $"ffmpeg 오류: {LastLines(error, 1)}");
        }
    }

    ProcessStartInfo Start(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(ExecutablePath!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>원본 길이(초). ffmpeg 가 입력만 읽고 알려 주는 "Duration: 00:01:02.50" 을 쓴다. 모르면 0.</summary>
    async Task<double> DurationAsync(string input, CancellationToken cancellationToken)
    {
        using var probe = Process.Start(Start(["-hide_banner", "-i", input]));
        if (probe is null)
            return 0;
        var stdout = probe.StandardOutput.ReadToEndAsync(cancellationToken);
        var text = await probe.StandardError.ReadToEndAsync(cancellationToken);
        await stdout;
        await probe.WaitForExitAsync(cancellationToken);
        var at = text.IndexOf("Duration: ", StringComparison.Ordinal);
        return at >= 0 && TimeSpan.TryParse(text.AsSpan(at + 10, 11), System.Globalization.CultureInfo.InvariantCulture, out var length)
            ? length.TotalSeconds
            : 0;
    }

    static string? Locate()
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe");
        if (File.Exists(bundled))
            return bundled;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // PATH 에 잘못된 글자가 든 항목
            }
        }

        return null;
    }

    static string LastLines(string text, int count) =>
        string.Join(" / ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).TakeLast(count));
}
