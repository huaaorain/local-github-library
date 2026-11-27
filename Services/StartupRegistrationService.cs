using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace GitHubTopicReports.Services;

public sealed class StartupRegistrationService
{
    public const string DefaultValueName = "LocalGithubLibrary";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;

    public StartupRegistrationService(string? valueName = null)
    {
        _valueName = string.IsNullOrWhiteSpace(valueName)
            ? CreateInstallationValueName()
            : valueName;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        if (key?.GetValue(_valueName) is string value &&
            !string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return key?.GetValue(DefaultValueName) is string legacyValue &&
               LegacyValueMatchesCurrentInstallation(legacyValue);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("无法打开 Windows 开机启动注册表项。");
        if (!enabled)
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
            if (key.GetValue(DefaultValueName) is string legacyValue &&
                LegacyValueMatchesCurrentInstallation(legacyValue))
            {
                key.DeleteValue(DefaultValueName, throwOnMissingValue: false);
            }
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath) ||
            !File.Exists(executablePath) ||
            !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前程序路径无法用于设置开机自启。");
        }

        key.SetValue(_valueName, $"\"{executablePath}\"", RegistryValueKind.String);
        if (key.GetValue(DefaultValueName) is string currentLegacyValue &&
            LegacyValueMatchesCurrentInstallation(currentLegacyValue))
        {
            key.DeleteValue(DefaultValueName, throwOnMissingValue: false);
        }
    }

    private static string CreateInstallationValueName()
    {
        var directory = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(directory)))[..12];
        return $"{DefaultValueName}-{hash}";
    }

    private static bool LegacyValueMatchesCurrentInstallation(string value)
    {
        try
        {
            var path = value.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var legacyDirectory = Path.GetDirectoryName(
                Path.GetFullPath(path));
            return string.Equals(
                legacyDirectory,
                Path.GetFullPath(AppContext.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
