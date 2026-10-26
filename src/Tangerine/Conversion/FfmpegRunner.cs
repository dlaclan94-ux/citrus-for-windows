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

    public async Task RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        if (ExecutablePath is null)
            throw new InvalidOperationException("ffmpeg.exe 를 찾을 수 없습니다. 영상·오디오 변환에 필요합니다.");

        var start = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

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

        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var error = await stderr;
        await stdout;

        if (process.ExitCode != 0)
        {
            AppLog.Write($"ffmpeg 오류 (코드 {process.ExitCode}): {LastLines(error, 3)}");
            // 소리 없는 영상에서 '소리만' 을 고른 경우처럼 원본에 필요한 트랙이 없을 때
            throw new InvalidOperationException(error.Contains("does not contain any stream")
                ? "원본에 바꿀 소리(또는 영상)가 없습니다"
                : $"ffmpeg 오류: {LastLines(error, 1)}");
        }
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
