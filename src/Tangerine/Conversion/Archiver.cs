using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;
using Tangerine.Core;

namespace Tangerine.Conversion;

/// <summary>
/// 끌어 온 파일·폴더를 모두 묶어 ZIP 하나를 만든다. 반디집이 있으면 반디집(bz.exe)으로, 없으면 Windows 기본 기능으로.
/// 이름은 반디집 오른쪽 클릭 메뉴처럼: 하나면 그 이름, 여럿이면 들어 있던 폴더 이름.
/// </summary>
internal static class Archiver
{
    public static async Task<string> CreateAsync(IReadOnlyList<string> items)
    {
        var first = Path.GetFullPath(items[0]).TrimEnd(Path.DirectorySeparatorChar);
        var dir = Path.GetDirectoryName(first) ?? throw new InvalidOperationException("드라이브 전체는 압축할 수 없습니다");
        var name = items.Count > 1 ? Path.GetFileName(dir)
            : Directory.Exists(first) ? Path.GetFileName(first)
            : Path.GetFileNameWithoutExtension(first);
        if (string.IsNullOrWhiteSpace(name))
            name = "압축";

        var output = OutputPaths.Reserve(Path.Combine(dir, name + ".zip"), ".zip");
        try
        {
            if (Bandizip() is { } bz)
                await RunBandizipAsync(bz, output, items);
            else
                await Task.Run(() => WriteZip(output, items));
        }
        catch
        {
            TryDelete(output);
            throw;
        }
        return output;
    }

    static string? Bandizip()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Bandizip");
        var folder = key?.GetValue("ProgramFolder") as string ?? @"C:\Program Files\Bandizip";
        var bz = Path.Combine(folder, "bz.exe");
        return File.Exists(bz) ? bz : null;
    }

    static async Task RunBandizipAsync(string bz, string output, IReadOnlyList<string> items)
    {
        // c = 새로 만들기 (자리 잡아 둔 빈 파일은 덮어쓴다), -y = 묻지 않기
        var start = new ProcessStartInfo(bz) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "c", "-y", "-fmt:zip", output }.Concat(items))
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("반디집을 실행하지 못했습니다");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"반디집 오류 (코드 {process.ExitCode}): {(await stderr + await stdout).Trim().Split('\n').LastOrDefault()?.Trim()}");
    }

    /// <summary>반디집이 없을 때. 폴더는 그 이름 그대로 안에 넣는다.</summary>
    static void WriteZip(string output, IReadOnlyList<string> items)
    {
        using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var item in items.Select(i => Path.GetFullPath(i).TrimEnd(Path.DirectorySeparatorChar)))
        {
            if (File.Exists(item))
            {
                zip.CreateEntryFromFile(item, Path.GetFileName(item));
                continue;
            }

            var root = Path.GetDirectoryName(item)!;
            foreach (var dir in Directory.EnumerateDirectories(item, "*", SearchOption.AllDirectories).Prepend(item))
            {
                var files = Directory.GetFiles(dir);
                if (files.Length == 0 && !Directory.EnumerateDirectories(dir).Any())
                    zip.CreateEntry(Path.GetRelativePath(root, dir).Replace('\\', '/') + "/");
                foreach (var file in files)
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
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
