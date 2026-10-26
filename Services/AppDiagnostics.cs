using System.IO;
using System.Net.Http;
using System.Security.Authentication;
using System.Text;

namespace GitHubTopicReports.Services;

public static class AppDiagnostics
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static string ErrorLogPath =>
        Path.Combine(AppContext.BaseDirectory, "GitHubReports", "Data", "error.log");

    public static string GetUserMessage(Exception exception)
    {
        if (exception is GitHubNetworkException or GitHubApiException)
        {
            return exception.Message;
        }

        if (ContainsSslFailure(exception))
        {
            return "无法建立与 GitHub 的 TLS 安全连接。程序已改为直连 GitHub；请关闭或切换代理、VPN 后重试。";
        }

        if (exception is TaskCanceledException)
        {
            return "连接 GitHub 超时，请检查网络后重试。";
        }

        if (exception is HttpRequestException)
        {
            return $"无法连接 GitHub：{RootMessage(exception)}";
        }

        return exception.Message;
    }

    public static string LogException(string context, Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(ErrorLogPath)!;
            Directory.CreateDirectory(directory);
            var entry =
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}{Environment.NewLine}" +
                $"{exception}{Environment.NewLine}" +
                new string('-', 80) + Environment.NewLine;
            File.AppendAllText(ErrorLogPath, entry, Utf8NoBom);
        }
        catch
        {
            // Diagnostics must never hide the original failure.
        }

        return ErrorLogPath;
    }

    private static bool ContainsSslFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException ||
                current.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string RootMessage(Exception exception)
    {
        var current = exception;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }
}
