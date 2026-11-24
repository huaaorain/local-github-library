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
    public bool StartWithWindows { get; set; }
    public double WindowWidth { get; set; } = 1240;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; }
    public double CombinedLeftPanelWidth { get; set; } = 220;
    public double CombinedRightPanelWidth { get; set; } = 280;
    public double FindLeftPanelWidth { get; set; } = 220;
    public double FindRightPanelWidth { get; set; } = 280;
    public double GlobalLeftPanelWidth { get; set; } = 220;
    public double GlobalRightPanelWidth { get; set; } = 280;
    public double NotesLeftPanelWidth { get; set; } = 280;

    public AppSettings Copy() => new()
    {
        CloseAction = CloseAction,
        AskOnClose = AskOnClose,
        StartWithWindows = StartWithWindows,
        WindowWidth = WindowWidth,
        WindowHeight = WindowHeight,
        WindowMaximized = WindowMaximized,
        CombinedLeftPanelWidth = CombinedLeftPanelWidth,
        CombinedRightPanelWidth = CombinedRightPanelWidth,
        FindLeftPanelWidth = FindLeftPanelWidth,
        FindRightPanelWidth = FindRightPanelWidth,
        GlobalLeftPanelWidth = GlobalLeftPanelWidth,
        GlobalRightPanelWidth = GlobalRightPanelWidth,
        NotesLeftPanelWidth = NotesLeftPanelWidth
    };
}
