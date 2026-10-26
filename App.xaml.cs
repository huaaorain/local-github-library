using System.Windows;
using System.Windows.Threading;
using GitHubTopicReports.Services;

namespace GitHubTopicReports;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
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
