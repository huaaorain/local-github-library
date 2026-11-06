using System.Windows;
using GitHubTopicReports.Models;

namespace GitHubTopicReports;

public partial class CloseBehaviorDialog : Window
{
    public CloseBehaviorDialog(bool askOnClose = true)
    {
        InitializeComponent();
        AskAgainCheckBox.IsChecked = askOnClose;
    }

    public AppCloseAction? SelectedAction { get; private set; }
    public bool AskOnClose { get; private set; }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        Complete(AppCloseAction.MinimizeToTray);

    private void ExitButton_Click(object sender, RoutedEventArgs e) =>
        Complete(AppCloseAction.Exit);

    private void Complete(AppCloseAction action)
    {
        AskOnClose = AskAgainCheckBox.IsChecked == true;
        SelectedAction = action;
        DialogResult = true;
    }
}
