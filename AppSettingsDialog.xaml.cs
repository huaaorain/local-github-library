using System.Windows;
using GitHubTopicReports.Models;

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
    public AppSettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        CurrentSettings = new AppSettings
        {
            CloseAction = settings.CloseAction,
            AskOnClose = settings.AskOnClose
        };
        MinimizeToTrayRadio.IsChecked =
            CurrentSettings.CloseAction == AppCloseAction.MinimizeToTray;
        ExitRadio.IsChecked = CurrentSettings.CloseAction == AppCloseAction.Exit;
        AskOnCloseCheckBox.IsChecked = CurrentSettings.AskOnClose;
    }

    public AppSettings CurrentSettings { get; private set; }
    public AppSettingsAction RequestedAction { get; private set; }

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
        CurrentSettings = new AppSettings
        {
            CloseAction = ExitRadio.IsChecked == true
                ? AppCloseAction.Exit
                : AppCloseAction.MinimizeToTray,
            AskOnClose = AskOnCloseCheckBox.IsChecked == true
        };
        RequestedAction = action;
        DialogResult = true;
    }
}
