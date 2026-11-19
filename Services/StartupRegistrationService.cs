using System.IO;
using Microsoft.Win32;

namespace GitHubTopicReports.Services;

public sealed class StartupRegistrationService
{
    public const string DefaultValueName = "LocalGithubLibrary";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;

    public StartupRegistrationService(string valueName = DefaultValueName)
    {
        _valueName = valueName;
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(_valueName) is string value &&
               !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException("无法打开 Windows 开机启动注册表项。");
        if (!enabled)
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
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
    }
}
