namespace GitHubTopicReports.Models;

public enum AppCloseAction
{
    MinimizeToTray,
    Exit
}

public sealed class AppSettings
{
    public AppCloseAction CloseAction { get; set; } = AppCloseAction.MinimizeToTray;
    public bool AskOnClose { get; set; } = true;
}
