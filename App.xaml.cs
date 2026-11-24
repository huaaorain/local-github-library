using System.Windows;
using System.Windows.Threading;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GitHubTopicReports.Services;

namespace GitHubTopicReports;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var instanceKey = Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    Path.GetFullPath(AppContext.BaseDirectory)
                        .ToUpperInvariant())))[..24];
        _singleInstanceMutex = new Mutex(
            initiallyOwned: true,
            $@"Local\LocalGitHubLibrary-{instanceKey}",
            out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            MessageBox.Show(
                "Local GitHub Library 已经在运行。请切换到已打开的窗口，避免多个实例同时写入笔记数据。",
                "程序已经运行",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            var libraryRoot = Path.Combine(AppContext.BaseDirectory, "GitHubReports");
            var settings = new AppSettingsService(libraryRoot).Load();
            new StartupRegistrationService().SetEnabled(settings.StartWithWindows);
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("同步开机自启设置", exception);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_singleInstanceMutex is not null)
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowUnexpectedError("界面线程异常", e.Exception);
    }

    private static void CurrentDomain_UnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppDiagnostics.LogException("应用域未处理异常", exception);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        AppDiagnostics.LogException("后台任务异常", e.Exception);
        e.SetObserved();
    }

    private static void ShowUnexpectedError(string context, Exception exception)
    {
        var logPath = AppDiagnostics.LogException(context, exception);
        MessageBox.Show(
            $"{AppDiagnostics.GetUserMessage(exception)}\n\n" +
            $"完整错误信息已保存到：\n{logPath}",
            "程序遇到错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
