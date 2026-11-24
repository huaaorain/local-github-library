using System.Windows;

namespace GitHubTopicReports;

public partial class NotePasswordDialog : Window
{
    private readonly bool _requireConfirmation;

    public NotePasswordDialog(
        string title,
        string prompt,
        bool requireConfirmation,
        string confirmText = "解锁")
    {
        InitializeComponent();
        Title = title;
        DialogTitleText.Text = title;
        PromptText.Text = prompt;
        ConfirmButton.Content = confirmText;
        _requireConfirmation = requireConfirmation;
        ConfirmationPanel.Visibility = requireConfirmation
            ? Visibility.Visible
            : Visibility.Collapsed;
        Height = requireConfirmation ? 400 : 320;
        MinHeight = Height;
        Loaded += (_, _) => PasswordInput.Focus();
    }

    public string Password { get; private set; } = "";

    private void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        var password = PasswordInput.Password;
        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("请输入密码。");
            return;
        }

        if (_requireConfirmation)
        {
            if (password.Length < 8)
            {
                ShowError("新密码至少需要 8 个字符。");
                return;
            }

            if (!string.Equals(
                    password,
                    ConfirmationInput.Password,
                    StringComparison.Ordinal))
            {
                ShowError("两次输入的密码不一致。");
                return;
            }
        }

        Password = password;
        PasswordInput.Clear();
        ConfirmationInput.Clear();
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
