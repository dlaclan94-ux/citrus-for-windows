using System;
using System.IO;
using System.Text.Json;

namespace Tangerine.Services;

/// <summary>%APPDATA%\Tangerine\settings.json. 파일이 없거나 깨졌으면 기본값.</summary>
internal sealed class AppSettings
{
    public bool Enabled { get; set; } = true;
    public bool ExplorerOnly { get; set; } = true;
    public bool WelcomeShown { get; set; }

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tangerine", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLog.Write($"설정을 읽지 못해 기본값을 씁니다: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"설정을 저장하지 못했습니다: {ex.Message}");
        }
    }
}
