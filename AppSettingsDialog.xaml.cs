using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Navigation;
using GitHubTopicReports.Models;
using GitHubTopicReports.Services;

namespace GitHubTopicReports;

public enum AppSettingsAction
{
    None,
    ImportLlm,
    ImportConfiguration,
    ExportConfiguration,
    OpenLibrary
}

public partial class AppSettingsDialog : Window
{
    private readonly GitHubSearchService _gitHub;

    public AppSettingsDialog(AppSettings settings, GitHubSearchService gitHub)
    {
        InitializeComponent();
        _gitHub = gitHub;
        CurrentSettings = settings.Copy();
        MinimizeToTrayRadio.IsChecked =
            CurrentSettings.CloseAction == AppCloseAction.MinimizeToTray;
        ExitRadio.IsChecked = CurrentSettings.CloseAction == AppCloseAction.Exit;
        AskOnCloseCheckBox.IsChecked = CurrentSettings.AskOnClose;
        StartWithWindowsCheckBox.IsChecked = CurrentSettings.StartWithWindows;
        Loaded += AppSettingsDialog_Loaded;
    }

    public AppSettings CurrentSettings { get; private set; }
    public AppSettingsAction RequestedAction { get; private set; }
    public bool AccountChanged { get; private set; }

    private async void AppSettingsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_gitHub.HasToken)
        {
            ShowSignedOutState();
            return;
        }

        try
        {
            var login = await _gitHub.GetCurrentLoginAsync();
            ShowSignedInState(login);
        }
        catch (GitHubApiException exception) when (
            exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            GitHubAccountStatusText.Text = "登录已失效";
            GitHubAccountHintText.Text = "请重新登录 GitHub 后继续搜索私有仓库。";
        }
        catch (Exception exception)
        {
            GitHubAccountStatusText.Text = "登录凭据已加载";
            GitHubAccountHintText.Text =
                $"暂时无法验证账号：{AppDiagnostics.GetUserMessage(exception)}";
        }
    }

    private void GitHubLoginButton_Click(object sender, RoutedEventArgs e)
    {
        GitHubLoginPanel.Visibility = Visibility.Visible;
        GitHubLoginErrorText.Visibility = Visibility.Collapsed;
        GitHubTokenPasswordBox.Clear();
        GitHubLoginPanel.BringIntoView();
        GitHubTokenPasswordBox.Focus();
    }

    private void CancelGitHubLoginButton_Click(object sender, RoutedEventArgs e)
    {
        GitHubLoginPanel.Visibility = Visibility.Collapsed;
        GitHubLoginErrorText.Visibility = Visibility.Collapsed;
        GitHubTokenPasswordBox.Clear();
    }

    private async void ConfirmGitHubLoginButton_Click(object sender, RoutedEventArgs e)
    {
        var token = GitHubTokenPasswordBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            ShowLoginError("请输入 GitHub Personal Access Token。");
            return;
        }

        SetLoginBusyState(true);
        GitHubLoginErrorText.Visibility = Visibility.Collapsed;
        try
        {
            var login = await _gitHub.SignInAsync(token);
            AccountChanged = true;
            GitHubTokenPasswordBox.Clear();
            GitHubLoginPanel.Visibility = Visibility.Collapsed;
            ShowSignedInState(login);
        }
        catch (Exception exception)
        {
            ShowLoginError(AppDiagnostics.GetUserMessage(exception));
        }
        finally
        {
            SetLoginBusyState(false);
        }
    }

    private void GitHubLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var message = _gitHub.SignOut();
            AccountChanged = true;
            ShowSignedOutState();
            GitHubAccountHintText.Text = message;
        }
        catch (Exception exception)
        {
            ShowLoginError(AppDiagnostics.GetUserMessage(exception));
        }
    }

    private void SetLoginBusyState(bool busy)
    {
        GitHubTokenPasswordBox.IsEnabled = !busy;
        ConfirmGitHubLoginButton.IsEnabled = !busy;
        GitHubLoginButton.IsEnabled = !busy;
    }

    private void ShowSignedInState(string? login)
    {
        GitHubAccountStatusText.Text = string.IsNullOrWhiteSpace(login)
            ? "已登录 GitHub"
            : $"已登录：@{login}";
        GitHubAccountHintText.Text = "可搜索账号有权限访问的公开与私有仓库。";
        GitHubLoginButton.Visibility = Visibility.Collapsed;
        GitHubLogoutButton.Visibility = Visibility.Visible;
    }

    private void ShowSignedOutState()
    {
        GitHubAccountStatusText.Text = "未登录";
        GitHubAccountHintText.Text = "目前只搜索公开仓库。登录后可搜索有权限访问的私有仓库。";
        GitHubLoginButton.Visibility = Visibility.Visible;
        GitHubLogoutButton.Visibility = Visibility.Collapsed;
        GitHubLoginPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowLoginError(string message)
    {
        GitHubLoginErrorText.Text = message;
        GitHubLoginErrorText.Visibility = Visibility.Visible;
    }

    private void CreateGitHubTokenLink_RequestNavigate(
        object sender,
        RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = e.Uri.AbsoluteUri,
            UseShellExecute = true
        });
        e.Handled = true;
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e) =>
        Complete(AppSettingsAction.None);

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string action } &&
            Enum.TryParse<AppSettingsAction>(action, out var requestedAction))
        {
            Complete(requestedAction);
        }
    }

    private void Complete(AppSettingsAction action)
    {
        CurrentSettings.CloseAction = ExitRadio.IsChecked == true
            ? AppCloseAction.Exit
            : AppCloseAction.MinimizeToTray;
        CurrentSettings.AskOnClose = AskOnCloseCheckBox.IsChecked == true;
        CurrentSettings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        RequestedAction = action;
        DialogResult = true;
    }
}
