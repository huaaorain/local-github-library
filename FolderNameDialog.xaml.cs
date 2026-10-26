using System.Windows;
using System.Windows.Input;

namespace GitHubTopicReports;

public partial class FolderNameDialog : Window
{
    public FolderNameDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        PromptText.Text = prompt;
        FolderNameTextBox.Text = initialValue;
        Loaded += (_, _) =>
        {
            FolderNameTextBox.Focus();
            FolderNameTextBox.SelectAll();
        };
    }

    public string FolderName => FolderNameTextBox.Text.Trim();

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderName))
        {
            MessageBox.Show(
                this,
                "目录名称不能为空。",
                "无法保存",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            FolderNameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void FolderNameTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
        }
    }
}
