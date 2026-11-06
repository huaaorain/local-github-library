using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class AppSettingsService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettingsService(string libraryRoot)
    {
        SettingsDirectory = Path.Combine(Path.GetFullPath(libraryRoot), "Data");
        SettingsFilePath = Path.Combine(SettingsDirectory, "app-settings.json");
        Directory.CreateDirectory(SettingsDirectory);
    }

    public string SettingsDirectory { get; }
    public string SettingsFilePath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        using var stream = File.OpenRead(SettingsFilePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions)
            ?? throw new InvalidDataException("软件设置文件内容为空。");
        return settings;
    }

    public void Save(AppSettings settings)
    {
        var temporaryPath = SettingsFilePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(settings, JsonOptions),
            Utf8NoBom);
        File.Move(temporaryPath, SettingsFilePath, overwrite: true);
    }
}
