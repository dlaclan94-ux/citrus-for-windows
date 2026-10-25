using System;
using System.IO;

namespace Tangerine.Services;

/// <summary>%LOCALAPPDATA%\Tangerine\tangerine.log 에 한 줄씩 남긴다. 실패한 변환의 자세한 이유를 볼 때 쓴다.</summary>
internal static class AppLog
{
    static readonly object Gate = new();

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tangerine");

    static string FilePath => Path.Combine(Folder, "tangerine.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 1_000_000)
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
