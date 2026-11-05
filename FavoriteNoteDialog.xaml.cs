using System.Windows;

namespace GitHubTopicReports;

public partial class FavoriteNoteDialog : Window
{
    public FavoriteNoteDialog(string repositoryFullName, string existingNote)
    {
        InitializeComponent();
        RepositoryText.Text = repositoryFullName;
        NoteTextBox.Text = existingNote;
        DeleteNoteButton.Visibility = string.IsNullOrWhiteSpace(existingNote)
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateCount();
        Loaded += (_, _) =>
        {
            NoteTextBox.Focus();
            NoteTextBox.CaretIndex = NoteTextBox.Text.Length;
        };
    }

    public string Note { get; private set; } = "";

    public bool DeleteRequested { get; private set; }

    private void NoteTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        UpdateCount();

    private void UpdateCount()
    {
        if (NoteCountText is not null)
        {
            NoteCountText.Text = $"{NoteTextBox.Text.Length:N0} / 4000";
        }
    }

    private void SaveNoteButton_Click(object sender, RoutedEventArgs e)
    {
        Note = NoteTextBox.Text.Trim();
        DialogResult = true;
    }

    private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            "确定删除这条收藏备注吗？",
            "删除备注",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteRequested = true;
        Note = "";
        DialogResult = true;
    }
}
