using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using GitHubTopicReports.Models;
using GitHubTopicReports.Services;

namespace GitHubTopicReports;

public partial class MainWindow : Window
{
    private readonly GitHubSearchService _gitHub = new();
    private readonly ReportLibraryService _library = new();
    private readonly FavoritesService _favorites;
    private readonly GlobalLeaderboardService _globalLeaderboard;
    private readonly ConfigurationPackageService _configurationPackages;
    private readonly LlmPackageService _llmPackageService;
    private readonly LlmPromptService _llmPromptService;
    private readonly LocalLlmService _localLlm;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _standaloneSearchCancellation;
    private CancellationTokenSource? _findSummaryCancellation;
    private CancellationTokenSource? _combinedSummaryCancellation;
    private CancellationTokenSource? _globalSummaryCancellation;
    private List<HistoryEntry> _history = [];
    private List<HistoryEntry> _globalHistory = [];
    private List<RepositoryRecord> _allRepositories = [];
    private List<FavoriteFolder> _favoriteFolders = [];
    private RepositoryRecord? _selectedRepository;
    private FavoriteFolder? _selectedFavoriteFolder;
    private FavoriteRepository? _selectedFavoriteRepository;
    private GlobalLeaderboardResult? _currentGlobalLeaderboard;
    private GlobalLeaderboardResult? _increaseGlobalCache;
    private GlobalLeaderboardResult? _currentGlobalCache;
    private List<RepositoryRecord> _currentStarRepositories = [];
    private GlobalLeaderboardEntry? _selectedGlobalEntry;
    private List<FavoriteFolder> _searchFolders = [];
    private List<RepositoryRecord> _standaloneSearchResults = [];
    private FavoriteFolder? _selectedSearchFolder;
    private RepositoryRecord? _selectedStandaloneRepository;
    private FavoriteRepository? _selectedCombinedFavoriteRepository;
    private string _standaloneDetailUrl = "";
    private bool _workspaceReady;
    private bool _standaloneSearchBusy;
    private string _currentKeyword = "";

    public MainWindow()
    {
        _favorites = new FavoritesService(_library.LibraryRoot);
        _globalLeaderboard = new GlobalLeaderboardService(_library.LibraryRoot);
        _configurationPackages = new ConfigurationPackageService(_library.LibraryRoot);
        _llmPackageService = new LlmPackageService(_library.LibraryRoot);
        _llmPromptService = new LlmPromptService(_library.LibraryRoot);
        _localLlm = new LocalLlmService(_llmPackageService, _llmPromptService);
        InitializeComponent();
        ResultCountComboBox.ItemsSource = new[] { "10", "20", "50", "100" };
        ResultCountComboBox.Text = "10";
        GlobalPeriodComboBox.ItemsSource = GlobalLeaderboardPeriodOption.All;
        GlobalPeriodComboBox.SelectedIndex = 1;
        GlobalModeComboBox.ItemsSource = GlobalLeaderboardModeOption.All;
        GlobalModeComboBox.SelectedIndex = 0;
        GlobalCountComboBox.ItemsSource = new[] { "10", "20", "50", "100" };
        GlobalCountComboBox.Text = "10";
        StandaloneSearchCountComboBox.ItemsSource = new[] { "10", "20", "50", "100" };
        StandaloneSearchCountComboBox.Text = "20";
        VersionText.Text = $"版本 {typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.2.0"}";
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CombineFavoriteWorkspace();
        await RefreshHistoryAsync();
        await RefreshSearchDirectoriesAsync();
        await RefreshLlmStatusAsync();
        StandaloneSearchKeywordTextBox.Focus();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _standaloneSearchCancellation?.Cancel();
        _standaloneSearchCancellation?.Dispose();
        _findSummaryCancellation?.Cancel();
        _findSummaryCancellation?.Dispose();
        _combinedSummaryCancellation?.Cancel();
        _combinedSummaryCancellation?.Dispose();
        _globalSummaryCancellation?.Cancel();
        _globalSummaryCancellation?.Dispose();
        _gitHub.Dispose();
        _globalLeaderboard.Dispose();
        _localLlm.Dispose();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunSearchAsync();
    }

    private async void KeywordTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunSearchAsync();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _searchCancellation?.Cancel();
        StatusText.Text = "正在取消查询...";
    }

    private async Task RunSearchAsync()
    {
        if (!TryGetRequestedResultCount(out var requestedCount))
        {
            return;
        }

        var keyword = KeywordTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            MessageBox.Show(
                this,
                "请输入要查找的主题。若要查看全站 Star 增量排行，请切换到“总榜”页签。",
                "请输入主题",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            KeywordTextBox.Focus();
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;

        SetBusyState(true);
        StatusText.Text = $"正在查询“{keyword}”的前 {requestedCount:N0} 个项目...";
        ReportPathText.Text = "";

        try
        {
            var generatedAt = DateTimeOffset.Now;
            _currentKeyword = keyword;
            _allRepositories = (await _gitHub.SearchAsync(
                keyword,
                requestedCount,
                cancellationToken)).ToList();

            if (_allRepositories.Count == 0)
            {
                CurrentGrid.ItemsSource = Array.Empty<RepositoryRecord>();
                GrowthGrid.ItemsSource = Array.Empty<RepositoryRecord>();
                ClearRepositoryDetails();
                CurrentKeywordText.Text = keyword;
                ResultSummaryText.Text = "GitHub 没有返回符合条件的仓库。";
                StatusText.Text = "未找到记录，未生成报告。";
                UpdateFavoriteActionState();
                return;
            }

            var growth = await _library.CalculateWeeklyGrowthAsync(
                keyword,
                _allRepositories,
                generatedAt,
                requestedCount,
                cancellationToken);
            var currentTop = _allRepositories.Take(requestedCount).ToList();
            GrowthNoticeText.Text = growth.Message;

            var saved = await _library.SaveReportAsync(
                keyword,
                generatedAt,
                _allRepositories,
                growth,
                cancellationToken);

            CurrentGrid.ItemsSource = currentTop;
            GrowthGrid.ItemsSource = growth.Repositories;
            CurrentKeywordText.Text = keyword;
            ResultSummaryText.Text =
                $"指定主题 · 显示 {currentTop.Count:N0} 条 · " +
                $"本地周增长 {growth.Repositories.Count:N0} 条";
            LastGeneratedText.Text = $"生成于 {saved.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            StatusText.Text = _gitHub.HasToken
                ? "查询和报告保存完成。"
                : "查询和报告保存完成。当前使用匿名 GitHub API；配置 Token 可提高额度。";
            ReportPathText.Text = saved.FilePath;
            ReportPathText.ToolTip = saved.FilePath;

            if (currentTop.Count > 0)
            {
                CurrentGrid.SelectedIndex = 0;
            }

            await RefreshHistoryAsync();
            UpdateFavoriteActionState();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "查询已取消。";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("GitHub 搜索", exception);
            var message = AppDiagnostics.GetUserMessage(exception);
            StatusText.Text = message;
            MessageBox.Show(
                this,
                $"{message}\n\n诊断日志：\n{logPath}",
                "查询失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusyState(false);
        }
    }

    private void SetBusyState(bool busy)
    {
        SearchButton.IsEnabled = !busy;
        KeywordTextBox.IsEnabled = !busy;
        ResultCountComboBox.IsEnabled = !busy;
        CancelButton.IsEnabled = busy;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private bool TryGetRequestedResultCount(out int count)
    {
        if (int.TryParse(ResultCountComboBox.Text.Trim(), out count) &&
            count is >= 1 and <= 1000)
        {
            return true;
        }

        MessageBox.Show(
            this,
            "显示数量必须是 1 到 1000 之间的整数。",
            "数量无效",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        ResultCountComboBox.Focus();
        return false;
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            _history = (await _library.LoadHistoryAsync()).ToList();
            HistoryList.ItemsSource = _history;
            HistoryCountText.Text = $"{_history.Count:N0} 份";
            RefreshGlobalHistory();

            if (_history.Count > 0 && HistoryList.SelectedIndex < 0)
            {
                HistoryList.SelectedIndex = 0;
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"读取查询记录失败：{exception.Message}";
        }
    }

    private void RefreshGlobalHistory()
    {
        var selectedPath = (GlobalHistoryList.SelectedItem as HistoryEntry)?.FilePath;
        _globalHistory = _history
            .Where(entry => entry.Keyword.StartsWith("总榜-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.Timestamp)
            .ThenByDescending(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        GlobalHistoryList.ItemsSource = _globalHistory;
        GlobalHistoryCountText.Text = $"{_globalHistory.Count:N0} 份";

        if (_globalHistory.Count == 0)
        {
            OpenGlobalHistoryButton.IsEnabled = false;
            return;
        }

        GlobalHistoryList.SelectedItem =
            _globalHistory.FirstOrDefault(entry => entry.FilePath == selectedPath)
            ?? _globalHistory[0];
    }

    private void GlobalHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenGlobalHistoryButton.IsEnabled = GlobalHistoryList.SelectedItem is HistoryEntry;
    }

    private void GlobalHistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GlobalHistoryList.SelectedItem is HistoryEntry entry)
        {
            OpenWithShell(entry.FilePath);
        }
    }

    private void OpenGlobalHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (GlobalHistoryList.SelectedItem is HistoryEntry entry)
        {
            OpenWithShell(entry.FilePath);
        }
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenSelectedReportButton.IsEnabled = HistoryList.SelectedItem is HistoryEntry;
    }

    private void OpenSelectedReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryEntry entry)
        {
            OpenWithShell(entry.FilePath);
        }
    }

    private void HistoryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (HistoryList.SelectedItem is HistoryEntry entry)
        {
            OpenWithShell(entry.FilePath);
        }
    }

    private void RepositoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && grid.SelectedItem is RepositoryRecord repository)
        {
            ShowRepositoryDetails(repository);
        }
    }

    private void ShowRepositoryDetails(RepositoryRecord repository)
    {
        _selectedRepository = repository;
        DetailNameText.Text = repository.FullName;
        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(repository.Description)
            ? "无项目描述。"
            : repository.Description;
        LoadFindSummary(repository.FullName, repository.Description, repository.Topics);
        DetailStatsText.Text =
            $"Star：{repository.Stars:N0}\n" +
            $"Fork：{repository.Forks:N0}\n" +
            $"Open Issues：{repository.OpenIssues:N0}\n" +
            $"Language：{repository.Language}\n" +
            $"License：{repository.License}";
        DetailTopicsText.Text = $"Topics：{repository.TopicsDisplay}";
        DetailDatesText.Text =
            $"创建：{FormatDate(repository.CreatedAt)}\n" +
            $"更新：{FormatDate(repository.UpdatedAt)}\n" +
            $"最近推送：{FormatDate(repository.PushedAt)}";
        OpenRepositoryButton.IsEnabled = true;
        UpdateFavoriteActionState();
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";

    private void ClearRepositoryDetails()
    {
        _selectedRepository = null;
        DetailNameText.Text = "选择一个仓库查看详情";
        DetailDescriptionText.Text = "查询结果会显示在这里。";
        _findSummaryCancellation?.Cancel();
        DetailSummaryText.Text = "选择项目后自动读取 README 简介。";
        DetailLlmText.Text = "导入本地 LLM 包后自动生成。";
        DetailStatsText.Text = "";
        DetailTopicsText.Text = "";
        DetailDatesText.Text = "";
        OpenRepositoryButton.IsEnabled = false;
        UpdateFavoriteActionState();
    }

    private async void LoadFindSummary(
        string fullName,
        string fallbackDescription,
        IReadOnlyList<string> topics)
    {
        _findSummaryCancellation?.Cancel();
        _findSummaryCancellation?.Dispose();
        _findSummaryCancellation = new CancellationTokenSource();
        var cancellationToken = _findSummaryCancellation.Token;
        DetailSummaryText.Text = "正在读取项目 README 简介...";

        try
        {
            var summary = await _gitHub.GetProjectSummaryAsync(
                fullName,
                fallbackDescription,
                cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                DetailSummaryText.Text = summary;
                await StreamLlmInsightAsync(
                    fullName,
                    fallbackDescription,
                    topics,
                    summary,
                    DetailLlmText,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Selection changed before the README request completed.
        }
    }

    private void RepositoryGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedRepository is not null)
        {
            OpenWithShell(_selectedRepository.HtmlUrl);
        }
    }

    private void OpenRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRepository is not null)
        {
            OpenWithShell(_selectedRepository.HtmlUrl);
        }
    }

    private void OpenLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        _library.OpenLibrary();
    }

    private void ImportLlmPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LlmSettingsDialog(
            _llmPackageService,
            _llmPromptService,
            _localLlm,
            GetAppVersion)
        {
            Owner = this
        };
        dialog.ShowDialog();
        UpdateLlmStatus();
        if (dialog.PromptChanged)
        {
            StatusText.Text = "LLM 提示词已更新，下一次生成时生效。";
        }
    }

    private async Task RefreshLlmStatusAsync()
    {
        try
        {
            await _localLlm.RefreshPackageAsync();
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("读取本地 LLM 状态", exception);
        }

        UpdateLlmStatus();
    }

    private void UpdateLlmStatus()
    {
        if (_localLlm.ActivePackage is not { } package)
        {
            LlmStatusText.Text = "本地 LLM：未安装（使用 README 摘录）";
            return;
        }

        LlmStatusText.Text =
            $"本地 LLM：{package.Manifest.DisplayName}（{FormatBytes(package.ModelSize)}）";
    }

    private async void ExportConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 GitHub 主题报告库配置",
            Filter = "GitHubTopicReports 配置包 (*.ghconfig)|*.ghconfig|ZIP 文件 (*.zip)|*.zip",
            DefaultExt = ".ghconfig",
            AddExtension = true,
            InitialDirectory = AppContext.BaseDirectory,
            FileName = $"GitHubTopicReports-{DateTime.Now:yyyyMMdd-HHmmss}.ghconfig",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetConfigurationButtonsEnabled(false);
        StatusText.Text = "正在导出所有记录、收藏和报告...";
        try
        {
            var result = await _configurationPackages.ExportAsync(
                dialog.FileName,
                GetAppVersion(),
                CancellationToken.None);
            StatusText.Text = $"配置已导出：{result.FilePath}";
            MessageBox.Show(
                this,
                $"导出完成。\n\n文件：{result.FilePath}\n" +
                $"文件数量：{result.FileCount:N0}\n" +
                $"原始数据大小：{FormatBytes(result.TotalBytes)}\n\n" +
                "GitHub Token 和错误日志未包含在配置包中。",
                "配置导出完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("导出配置", exception);
            var message = AppDiagnostics.GetUserMessage(exception);
            StatusText.Text = message;
            MessageBox.Show(
                this,
                $"{message}\n\n诊断日志：\n{logPath}",
                "配置导出失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetConfigurationButtonsEnabled(true);
        }
    }

    private async void ImportConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 GitHub 主题报告库配置",
            Filter = "GitHubTopicReports 配置包 (*.ghconfig)|*.ghconfig|ZIP 文件 (*.zip)|*.zip|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            "导入会替换当前电脑上的报告、查询记录、收藏和 Star 快照。\n\n" +
            "程序会先自动备份当前数据。是否继续？",
            "确认导入配置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetConfigurationButtonsEnabled(false);
        StatusText.Text = "正在验证并导入配置...";
        try
        {
            var result = await _configurationPackages.ImportAsync(
                dialog.FileName,
                GetAppVersion(),
                CancellationToken.None);
            MessageBox.Show(
                this,
                $"导入完成，共导入 {result.ImportedFileCount:N0} 个文件，" +
                $"数据大小 {FormatBytes(result.ImportedBytes)}。\n\n" +
                $"导入前备份：\n{result.AutomaticBackupPath}\n\n" +
                "程序将重新启动以加载所有新数据。",
                "配置导入完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            RestartApplication();
        }
        catch (Exception exception)
        {
            SetConfigurationButtonsEnabled(true);
            var logPath = AppDiagnostics.LogException("导入配置", exception);
            var message = AppDiagnostics.GetUserMessage(exception);
            StatusText.Text = message;
            MessageBox.Show(
                this,
                $"{message}\n\n诊断日志：\n{logPath}",
                "配置导入失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SetConfigurationButtonsEnabled(bool enabled)
    {
        ImportConfigurationButton.IsEnabled = enabled;
        ExportConfigurationButton.IsEnabled = enabled;
    }

    private static string GetAppVersion() =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private static string FormatBytes(long bytes) =>
        bytes >= 1024L * 1024L * 1024L
            ? $"{bytes / 1024d / 1024d / 1024d:F2} GB"
            : bytes >= 1024L * 1024L
                ? $"{bytes / 1024d / 1024d:F2} MB"
                : $"{bytes / 1024d:F1} KB";

    private static void RestartApplication()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });
        }

        Application.Current.Shutdown();
    }

    private async void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_workspaceReady || !ReferenceEquals(e.Source, WorkspaceTabs))
        {
            return;
        }

        if (ReferenceEquals(WorkspaceTabs.SelectedItem, CombinedFavoritesTab))
        {
            await RefreshSearchDirectoriesAsync();
        }
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        SetDefaultPanelWidths(
            CombinedLeftColumn,
            CombinedCenterColumn,
            CombinedRightColumn,
            320);
        SetDefaultPanelWidths(
            FindLeftColumn,
            FindCenterColumn,
            FindRightColumn,
            310);
        SetDefaultPanelWidths(
            GlobalLeftColumn,
            GlobalCenterColumn,
            GlobalRightColumn,
            320);
        StatusText.Text = "已恢复默认面板布局。";
    }

    private static void SetDefaultPanelWidths(
        ColumnDefinition left,
        ColumnDefinition center,
        ColumnDefinition right,
        double rightWidth)
    {
        left.Width = new GridLength(240);
        center.Width = new GridLength(1, GridUnitType.Star);
        right.Width = new GridLength(rightWidth);
    }

    private void CombineFavoriteWorkspace()
    {
        if (_workspaceReady)
        {
            return;
        }

        WorkspaceTabs.Items.Remove(CombinedFavoritesTab);
        WorkspaceTabs.Items.Add(CombinedFavoritesTab);
        WorkspaceTabs.Items.Remove(OriginalFavoritesTab);
        WorkspaceTabs.SelectedItem = CombinedFavoritesTab;
        _workspaceReady = true;
    }

    private async void StandaloneSearchButton_Click(object sender, RoutedEventArgs e)
    {
        await RunStandaloneSearchAsync();
    }

    private async void StandaloneSearchKeywordTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunStandaloneSearchAsync();
        }
    }

    private void StandaloneSearchCancelButton_Click(object sender, RoutedEventArgs e)
    {
        _standaloneSearchCancellation?.Cancel();
        StatusText.Text = "正在取消搜索...";
    }

    private async Task RunStandaloneSearchAsync()
    {
        var keyword = StandaloneSearchKeywordTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(keyword))
        {
            MessageBox.Show(
                this,
                "请输入要搜索的关键词。",
                "缺少关键词",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            StandaloneSearchKeywordTextBox.Focus();
            return;
        }

        if (!TryGetCount(
                StandaloneSearchCountComboBox.Text,
                "搜索数量",
                out var resultCount))
        {
            return;
        }

        _standaloneSearchCancellation?.Cancel();
        _standaloneSearchCancellation?.Dispose();
        _standaloneSearchCancellation = new CancellationTokenSource();
        var cancellationToken = _standaloneSearchCancellation.Token;

        SetStandaloneSearchBusyState(true);
        StatusText.Text = $"正在搜索 GitHub 项目：{keyword}";
        try
        {
            _standaloneSearchResults = (await _gitHub.SearchAsync(
                keyword,
                resultCount,
                cancellationToken)).ToList();
            StandaloneSearchResultsGrid.ItemsSource = _standaloneSearchResults;
            CombinedContentTabs.SelectedIndex = 0;
            StandaloneSearchTitleText.Text = keyword;
            StandaloneSearchSummaryText.Text =
                $"GitHub 搜索结果 · {_standaloneSearchResults.Count:N0} 条 · " +
                "选择底部目录后可保存整份结果";
            _selectedStandaloneRepository = null;
            OpenStandaloneRepositoryButton.IsEnabled = false;
            StandaloneDetailNameText.Text = "选择一个仓库查看详情";
            StandaloneDetailDescriptionText.Text = "搜索结果会显示在这里。";
            StandaloneDetailStatsText.Text = "";
            StandaloneDetailTopicsText.Text = "";
            if (_standaloneSearchResults.Count > 0)
            {
                StandaloneSearchResultsGrid.SelectedIndex = 0;
            }
            else
            {
                ClearStandaloneDetails("GitHub 没有返回搜索结果。");
            }

            StatusText.Text = _standaloneSearchResults.Count == 0
                ? "GitHub 没有返回搜索结果。"
                : $"搜索完成，共 {_standaloneSearchResults.Count:N0} 条。";
            UpdateStandaloneSearchActionState();
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "搜索已取消。";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("独立搜索结果", exception);
            var message = AppDiagnostics.GetUserMessage(exception);
            StatusText.Text = message;
            MessageBox.Show(
                this,
                $"{message}\n\n诊断日志：\n{logPath}",
                "搜索失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetStandaloneSearchBusyState(false);
        }
    }

    private void SetStandaloneSearchBusyState(bool busy)
    {
        _standaloneSearchBusy = busy;
        StandaloneSearchButton.IsEnabled = !busy;
        StandaloneSearchKeywordTextBox.IsEnabled = !busy;
        StandaloneSearchCountComboBox.IsEnabled = !busy;
        StandaloneSearchCancelButton.IsEnabled = busy;
        UpdateStandaloneSearchActionState();
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private async Task RefreshSearchDirectoriesAsync(string? preferredFolderId = null)
    {
        try
        {
            var selectedId = preferredFolderId ?? _selectedSearchFolder?.Id;
            _favoriteFolders = (await _favorites.LoadAsync()).ToList();
            _searchFolders = _favoriteFolders;
            SearchDirectoryList.ItemsSource = _searchFolders;
            SearchDirectoryCountText.Text = $"{_searchFolders.Count:N0} 个";
            var previousDestinationFolderId =
                (SearchFavoriteFolderComboBox.SelectedItem as FavoriteFolder)?.Id;
            SearchFavoriteFolderComboBox.ItemsSource = _favoriteFolders;

            var selectedFolder = _searchFolders.FirstOrDefault(
                                     folder => folder.Id == selectedId)
                                 ?? _searchFolders.FirstOrDefault();
            SearchDirectoryList.SelectedItem = selectedFolder;
            _selectedSearchFolder = selectedFolder;
            SearchFavoriteFolderComboBox.SelectedItem =
                _favoriteFolders.FirstOrDefault(folder => folder.Id == preferredFolderId)
                ?? _favoriteFolders.FirstOrDefault(folder => folder.Id == previousDestinationFolderId)
                ?? selectedFolder;
            RenameCombinedFavoriteFolderButton.IsEnabled = selectedFolder is not null;
            CombinedFavoritesGrid.ItemsSource = selectedFolder?.Repositories
                .OrderByDescending(repository => repository.SavedAt)
                .ToList() ?? [];
            UpdateStandaloneSearchActionState();
            UpdateFavoriteActionState();
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("读取搜索目录", exception);
            StatusText.Text = AppDiagnostics.GetUserMessage(exception);
            MessageBox.Show(
                this,
                $"{AppDiagnostics.GetUserMessage(exception)}\n\n诊断日志：\n{logPath}",
                "收藏目录读取失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SearchDirectoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedSearchFolder = SearchDirectoryList.SelectedItem as FavoriteFolder;
        RenameCombinedFavoriteFolderButton.IsEnabled = _selectedSearchFolder is not null;
        CombinedFavoritesGrid.ItemsSource = _selectedSearchFolder?.Repositories
            .OrderByDescending(repository => repository.SavedAt)
            .ToList() ?? [];
        UpdateStandaloneSearchActionState();
    }

    private async void CreateSearchDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new FolderNameDialog(
            "新建收藏目录",
            "搜索结果可以收藏到这个目录中。")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var folder = await _favorites.CreateFolderAsync(dialog.FolderName);
            await RefreshSearchDirectoriesAsync(folder.Id);
            StatusText.Text = $"已创建收藏目录“{folder.Name}”。";
        }
        catch (DuplicateFavoriteFolderException)
        {
            const string message = "已存在同名目录，请勿重复建目录。";
            StatusText.Text = message;
            MessageBox.Show(
                this,
                message,
                "目录已存在",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("新建搜索目录", exception);
            StatusText.Text = AppDiagnostics.GetUserMessage(exception);
            MessageBox.Show(
                this,
                $"{AppDiagnostics.GetUserMessage(exception)}\n\n诊断日志：\n{logPath}",
                "新建目录失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void RenameCombinedFavoriteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSearchFolder is null)
        {
            return;
        }

        var dialog = new FolderNameDialog(
            "重命名收藏目录",
            "输入新的目录名称。",
            _selectedSearchFolder.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var folderId = _selectedSearchFolder.Id;
            await _favorites.RenameFolderAsync(folderId, dialog.FolderName);
            await RefreshSearchDirectoriesAsync(folderId);
            StatusText.Text = $"收藏目录已重命名为“{dialog.FolderName}”。";
        }
        catch (DuplicateFavoriteFolderException)
        {
            const string message = "已存在同名目录，请勿重复建目录。";
            StatusText.Text = message;
            MessageBox.Show(
                this,
                message,
                "目录已存在",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
    }

    private void OpenSearchDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        _library.OpenLibrary();
    }

    private void StandaloneSearchResultsGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _selectedStandaloneRepository = StandaloneSearchResultsGrid.SelectedItem as RepositoryRecord;
        if (_selectedStandaloneRepository is null)
        {
            if (CombinedContentTabs.SelectedIndex == 0)
            {
                ClearStandaloneDetails("在搜索结果中选择一个仓库查看详情。");
            }
            UpdateStandaloneSearchActionState();
            return;
        }

        if (CombinedContentTabs.SelectedIndex == 0)
        {
            ShowSearchRepositoryDetails(_selectedStandaloneRepository);
        }

        UpdateStandaloneSearchActionState();
    }

    private void ShowSearchRepositoryDetails(RepositoryRecord repository)
    {
        _standaloneDetailUrl = repository.HtmlUrl;
        OpenStandaloneRepositoryButton.IsEnabled = true;
        StandaloneDetailNameText.Text = repository.FullName;
        StandaloneDetailDescriptionText.Text =
            string.IsNullOrWhiteSpace(repository.Description)
                ? "无项目描述。"
                : repository.Description;
        LoadCombinedSummary(repository.FullName, repository.Description, repository.Topics);
        StandaloneDetailStatsText.Text =
            $"Star：{repository.Stars:N0}\n" +
            $"Fork：{repository.Forks:N0}\n" +
            $"Language：{repository.Language}\n" +
            $"License：{repository.License}";
        StandaloneDetailTopicsText.Text =
            $"Topics：{repository.TopicsDisplay}";
    }

    private void StandaloneSearchResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedStandaloneRepository is not null)
        {
            OpenWithShell(_selectedStandaloneRepository.HtmlUrl);
        }
    }

    private void OpenStandaloneRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_standaloneDetailUrl))
        {
            OpenWithShell(_standaloneDetailUrl);
        }
    }

    private async void SaveSearchResultsButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = _selectedSearchFolder;
        if (folder is null || _standaloneSearchResults.Count == 0)
        {
            return;
        }

        if (!_standaloneSearchResults.Any(repository => !folder.ContainsRepository(repository)))
        {
            SearchSaveFolderText.Text = $"当前搜索结果已全部收藏在“{folder.Name}”中。";
            UpdateStandaloneSearchActionState();
            return;
        }

        try
        {
            var keyword = StandaloneSearchKeywordTextBox.Text.Trim();
            var result = await _favorites.AddFavoritesAsync(
                folder.Id,
                keyword,
                _standaloneSearchResults);
            await RefreshSearchDirectoriesAsync(folder.Id);
            StatusText.Text =
                $"已收藏 {result.AddedCount:N0} 条到“{result.FolderName}”，" +
                $"跳过 {result.DuplicateCount:N0} 条重复记录。";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("保存搜索结果", exception);
            StatusText.Text = AppDiagnostics.GetUserMessage(exception);
            MessageBox.Show(
                this,
                $"{AppDiagnostics.GetUserMessage(exception)}\n\n诊断日志：\n{logPath}",
                "保存搜索结果失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void FavoriteSelectedSearchResultsButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var folder = _selectedSearchFolder;
        if (folder is null)
        {
            return;
        }

        var selectedRepositories = StandaloneSearchResultsGrid.SelectedItems
            .OfType<RepositoryRecord>()
            .ToList();
        if (selectedRepositories.Count == 0 &&
            _selectedStandaloneRepository is { } selectedRepository)
        {
            selectedRepositories.Add(selectedRepository);
        }
        if (selectedRepositories.Count == 0)
        {
            return;
        }

        if (!selectedRepositories.Any(repository => !folder.ContainsRepository(repository)))
        {
            SearchSaveFolderText.Text =
                $"所选结果已全部收藏在“{folder.Name}”中。";
            UpdateStandaloneSearchActionState();
            return;
        }

        try
        {
            var result = await _favorites.AddFavoritesAsync(
                folder.Id,
                StandaloneSearchKeywordTextBox.Text.Trim(),
                selectedRepositories);
            await RefreshSearchDirectoriesAsync(folder.Id);
            StatusText.Text =
                $"已收藏 {result.AddedCount:N0} 条到“{result.FolderName}”，" +
                $"跳过 {result.DuplicateCount:N0} 条重复记录。";
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
    }

    private void CombinedFavoritesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CombinedFavoritesGrid.SelectedItem is FavoriteRepository repository)
        {
            OpenWithShell(repository.HtmlUrl);
        }
    }

    private void CombinedFavoritesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _selectedCombinedFavoriteRepository =
            CombinedFavoritesGrid.SelectedItem as FavoriteRepository;
        if (CombinedContentTabs.SelectedIndex == 1)
        {
            if (_selectedCombinedFavoriteRepository is not null)
            {
                ShowFavoriteRepositoryDetails(_selectedCombinedFavoriteRepository);
            }
            else
            {
                ClearStandaloneDetails("在目录内容中选择一个仓库查看详情。");
            }
        }
    }

    private void CombinedContentTabs_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, CombinedContentTabs) || !IsLoaded)
        {
            return;
        }

        if (CombinedContentTabs.SelectedIndex == 1)
        {
            if (_selectedCombinedFavoriteRepository is not null)
            {
                ShowFavoriteRepositoryDetails(_selectedCombinedFavoriteRepository);
            }
            else
            {
                ClearStandaloneDetails("在目录内容中选择一个仓库查看详情。");
            }
        }
        else if (_selectedStandaloneRepository is not null)
        {
            ShowSearchRepositoryDetails(_selectedStandaloneRepository);
        }
        else
        {
            ClearStandaloneDetails("在搜索结果中选择一个仓库查看详情。");
        }

        UpdateStandaloneSearchActionState();
    }

    private void ShowFavoriteRepositoryDetails(FavoriteRepository repository)
    {
        _standaloneDetailUrl = repository.HtmlUrl;
        OpenStandaloneRepositoryButton.IsEnabled = true;
        StandaloneDetailNameText.Text = repository.FullName;
        StandaloneDetailDescriptionText.Text =
            string.IsNullOrWhiteSpace(repository.Description)
                ? "无项目描述。"
                : repository.Description;
        LoadCombinedSummary(repository.FullName, repository.Description, repository.Topics);
        StandaloneDetailStatsText.Text =
            $"Star：{repository.Stars:N0}\n" +
            $"Fork：{repository.Forks:N0}\n" +
            $"Open Issues：{repository.OpenIssues:N0}\n" +
            $"Language：{repository.Language}\n" +
            $"License：{repository.License}\n" +
            $"收藏时间：{repository.SavedAtDisplay}";
        StandaloneDetailTopicsText.Text =
            $"Topics：{repository.TopicsDisplay}\n" +
            $"来源关键词：{(string.IsNullOrWhiteSpace(repository.SourceKeyword) ? "未记录" : repository.SourceKeyword)}";
    }

    private void ClearStandaloneDetails(string message)
    {
        _standaloneDetailUrl = "";
        _combinedSummaryCancellation?.Cancel();
        OpenStandaloneRepositoryButton.IsEnabled = false;
        StandaloneDetailNameText.Text = "选择一个仓库查看详情";
        StandaloneDetailDescriptionText.Text = message;
        StandaloneDetailSummaryText.Text = "选择项目后自动读取 README 简介。";
        StandaloneDetailLlmText.Text = "导入本地 LLM 包后自动生成。";
        StandaloneDetailStatsText.Text = "";
        StandaloneDetailTopicsText.Text = "";
    }

    private async void LoadCombinedSummary(
        string fullName,
        string fallbackDescription,
        IReadOnlyList<string> topics)
    {
        _combinedSummaryCancellation?.Cancel();
        _combinedSummaryCancellation?.Dispose();
        _combinedSummaryCancellation = new CancellationTokenSource();
        var cancellationToken = _combinedSummaryCancellation.Token;
        StandaloneDetailSummaryText.Text = "正在读取项目 README 简介...";

        try
        {
            var summary = await _gitHub.GetProjectSummaryAsync(
                fullName,
                fallbackDescription,
                cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                StandaloneDetailSummaryText.Text = summary;
                await StreamLlmInsightAsync(
                    fullName,
                    fallbackDescription,
                    topics,
                    summary,
                    StandaloneDetailLlmText,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Selection or inner view changed before the request completed.
        }
    }

    private void UpdateStandaloneSearchActionState()
    {
        var searchViewActive = CombinedContentTabs.SelectedIndex == 0;
        var folder = _selectedSearchFolder;
        var selectedRepositories = StandaloneSearchResultsGrid.SelectedItems
            .OfType<RepositoryRecord>()
            .ToList();
        var newResultCount = folder is null
            ? 0
            : _standaloneSearchResults.Count(repository => !folder.ContainsRepository(repository));
        var newSelectedCount = folder is null
            ? 0
            : selectedRepositories.Count(repository => !folder.ContainsRepository(repository));
        var duplicateResultCount = _standaloneSearchResults.Count - newResultCount;

        SaveSearchResultsButton.IsEnabled =
            !_standaloneSearchBusy &&
            searchViewActive &&
            folder is not null &&
            newResultCount > 0;
        FavoriteSelectedSearchResultsButton.IsEnabled =
            !_standaloneSearchBusy &&
            searchViewActive &&
            folder is not null &&
            newSelectedCount > 0;
        SaveSearchResultsButton.Content = searchViewActive &&
                                          folder is not null &&
                                          newResultCount == 0 &&
                                          _standaloneSearchResults.Count > 0
            ? "搜索结果均已收藏"
            : "收藏全部搜索结果";
        FavoriteSelectedSearchResultsButton.Content = searchViewActive &&
                                                      folder is not null &&
                                                      newSelectedCount == 0 &&
                                                      selectedRepositories.Count > 0
            ? "所选结果均已收藏"
            : "收藏所选结果";
        SearchSaveFolderText.Text = !searchViewActive
            ? "当前正在查看目录内容；切换到“搜索结果”后可批量收藏。"
            : folder is null
                ? "请先选择一个收藏目录。"
                : newResultCount == 0 && _standaloneSearchResults.Count > 0
                    ? $"当前搜索结果已全部收藏在“{folder.Name}”中。"
                    : $"收藏到：{folder.Name}（已有 {folder.Repositories.Count:N0} 个仓库；" +
                      (duplicateResultCount == 0
                          ? "本页没有重复记录）"
                          : $"本页 {duplicateResultCount:N0} 条已收藏）");
    }

    private async Task RefreshFavoritesAsync(string? preferredFolderId = null)
    {
        try
        {
            var currentFolderId = preferredFolderId
                ?? _selectedFavoriteFolder?.Id
                ?? (SearchFavoriteFolderComboBox.SelectedItem as FavoriteFolder)?.Id;

            _favoriteFolders = (await _favorites.LoadAsync()).ToList();
            FavoriteFolderList.ItemsSource = _favoriteFolders;
            SearchFavoriteFolderComboBox.ItemsSource = _favoriteFolders;

            var selectedFolder = _favoriteFolders.FirstOrDefault(folder => folder.Id == currentFolderId)
                ?? _favoriteFolders.FirstOrDefault();
            FavoriteFolderList.SelectedItem = selectedFolder;
            SearchFavoriteFolderComboBox.SelectedItem = selectedFolder;

            RenameFavoriteFolderButton.IsEnabled = selectedFolder is not null;
            ShowSelectedFavoriteFolder();
            UpdateFavoriteActionState();
        }
        catch (Exception exception)
        {
            StatusText.Text = $"读取收藏失败：{exception.Message}";
            MessageBox.Show(
                this,
                exception.Message,
                "收藏文件读取失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void FavoriteFolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedFavoriteFolder = FavoriteFolderList.SelectedItem as FavoriteFolder;
        RenameFavoriteFolderButton.IsEnabled = _selectedFavoriteFolder is not null;
        ShowSelectedFavoriteFolder();
    }

    private void ShowSelectedFavoriteFolder()
    {
        _selectedFavoriteFolder = FavoriteFolderList.SelectedItem as FavoriteFolder;
        var repositories = _selectedFavoriteFolder?.Repositories
            .OrderByDescending(repository => repository.SavedAt)
            .ToList() ?? [];

        FavoritesGrid.ItemsSource = repositories;
        FavoritesFolderTitleText.Text = _selectedFavoriteFolder?.Name ?? "没有收藏目录";
        FavoritesFolderSummaryText.Text = repositories.Count == 0
            ? "这个目录还没有收藏仓库。"
            : "收藏仓库会保存在本地 favorites.json 文件中。";
        FavoritesCountText.Text = $"{repositories.Count:N0} 个仓库";
        _selectedFavoriteRepository = null;
        UpdateFavoriteButtons();
    }

    private async void CreateFavoriteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await CreateFolderAsync(switchToFavorites: false);
    }

    private async void CreateFolderFromSearch_Click(object sender, RoutedEventArgs e)
    {
        await CreateFolderAsync(switchToFavorites: false);
    }

    private async Task CreateFolderAsync(bool switchToFavorites)
    {
        var dialog = new FolderNameDialog(
            "新建收藏目录",
            "目录名称会显示在“我的收藏”中。")
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var folder = await _favorites.CreateFolderAsync(dialog.FolderName);
            await RefreshSearchDirectoriesAsync(folder.Id);
            StatusText.Text = $"已创建收藏目录“{folder.Name}”。";
            if (switchToFavorites)
            {
                WorkspaceTabs.SelectedIndex = 1;
            }
        }
        catch (DuplicateFavoriteFolderException)
        {
            ShowDuplicateFolderMessage();
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
    }

    private async void RenameFavoriteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFavoriteFolder is null)
        {
            return;
        }

        var dialog = new FolderNameDialog(
            "重命名收藏目录",
            "输入新的目录名称。",
            _selectedFavoriteFolder.Name)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var folderId = _selectedFavoriteFolder.Id;
            await _favorites.RenameFolderAsync(folderId, dialog.FolderName);
            await RefreshFavoritesAsync(folderId);
            StatusText.Text = $"收藏目录已重命名为“{dialog.FolderName}”。";
        }
        catch (DuplicateFavoriteFolderException)
        {
            ShowDuplicateFolderMessage();
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
    }

    private void SearchFavoriteFolderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateFavoriteActionState();
    }

    private async void FavoriteRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRepository is null ||
            SearchFavoriteFolderComboBox.SelectedItem is not FavoriteFolder folder)
        {
            return;
        }

        try
        {
            var result = await _favorites.AddFavoriteAsync(
                folder.Id,
                _currentKeyword,
                _selectedRepository);
            await RefreshSearchDirectoriesAsync(folder.Id);
            StatusText.Text = result.Message;
            SearchFavoriteHintText.Text = result.Message;
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
    }

    private void UpdateFavoriteActionState()
    {
        var folder = SearchFavoriteFolderComboBox.SelectedItem as FavoriteFolder;
        var repository = _selectedRepository;

        if (repository is null)
        {
            FavoriteRepositoryButton.IsEnabled = false;
            FavoriteRepositoryButton.Content = "收藏到所选目录";
            SearchFavoriteHintText.Text = "先在搜索结果中选择一个仓库。";
            return;
        }

        if (folder is null)
        {
            FavoriteRepositoryButton.IsEnabled = false;
            FavoriteRepositoryButton.Content = "收藏到所选目录";
            SearchFavoriteHintText.Text = "请选择一个收藏目录。";
            return;
        }

        var alreadyFavorited = folder.ContainsRepository(repository);
        FavoriteRepositoryButton.IsEnabled = !alreadyFavorited;
        FavoriteRepositoryButton.Content = alreadyFavorited
            ? "已收藏到当前目录"
            : "收藏到所选目录";
        SearchFavoriteHintText.Text = alreadyFavorited
            ? $"“{repository.FullName}”已经收藏在“{folder.Name}”中，不能重复收藏。"
            : $"将收藏到“{folder.Name}”。";
    }

    private void FavoritesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedFavoriteRepository = FavoritesGrid.SelectedItem as FavoriteRepository;
        FavoriteSelectionText.Text = _selectedFavoriteRepository is null
            ? "双击仓库可直接打开 GitHub。"
            : $"来源关键词：{(_selectedFavoriteRepository.SourceKeyword.Length == 0 ? "未记录" : _selectedFavoriteRepository.SourceKeyword)}";
        UpdateFavoriteButtons();
    }

    private void UpdateFavoriteButtons()
    {
        var hasSelection = _selectedFavoriteRepository is not null;
        OpenFavoriteRepositoryButton.IsEnabled = hasSelection;
        RemoveFavoriteButton.IsEnabled = hasSelection;
    }

    private void FavoritesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedFavoriteRepository is not null)
        {
            OpenWithShell(_selectedFavoriteRepository.HtmlUrl);
        }
    }

    private void OpenFavoriteRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFavoriteRepository is not null)
        {
            OpenWithShell(_selectedFavoriteRepository.HtmlUrl);
        }
    }

    private async void RemoveFavoriteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFavoriteFolder is null || _selectedFavoriteRepository is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"确定要从“{_selectedFavoriteFolder.Name}”移除“{_selectedFavoriteRepository.FullName}”吗？",
            "移除收藏",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var folderName = _selectedFavoriteFolder.Name;
            var repositoryName = _selectedFavoriteRepository.FullName;
            await _favorites.RemoveFavoriteAsync(
                _selectedFavoriteFolder.Id,
                _selectedFavoriteRepository.Id);
            await RefreshFavoritesAsync(_selectedFavoriteFolder.Id);
            StatusText.Text = $"已从“{folderName}”移除“{repositoryName}”。";
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
    }

    private void ShowFavoriteError(Exception exception)
    {
        var logPath = AppDiagnostics.LogException("收藏操作", exception);
        var message = AppDiagnostics.GetUserMessage(exception);
        StatusText.Text = message;
        MessageBox.Show(
            this,
            $"{message}\n\n诊断日志：\n{logPath}",
            "收藏操作失败",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void ShowDuplicateFolderMessage()
    {
        const string message = "已存在同名目录，请勿重复建目录。";
        StatusText.Text = message;
        MessageBox.Show(
            this,
            message,
            "目录已存在",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void CollectGlobalButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetCount(
                GlobalCountComboBox.Text,
                "总榜显示数量",
                out var requestedCount) ||
            GlobalModeComboBox.SelectedItem is not GlobalLeaderboardModeOption modeOption)
        {
            return;
        }

        SetGlobalBusyState(true);
        StatusText.Text = modeOption.Mode == GlobalLeaderboardMode.Current
            ? $"正在获取 GitHub 当前 Star 前 {requestedCount:N0} 名..."
            : "正在统计 Star 增量总榜...首次使用需要下载并解析每日榜单数据。";
        try
        {
            GlobalLeaderboardResult result;
            if (modeOption.Mode == GlobalLeaderboardMode.Current)
            {
                var fetchCount = Math.Max(100, requestedCount);
                var repositories = await _gitHub.SearchAsync(
                    null,
                    fetchCount,
                    CancellationToken.None);
                _currentStarRepositories = repositories.ToList();
                result = BuildCurrentLeaderboardResult(
                    _currentStarRepositories.Take(requestedCount).ToList());
            }
            else
            {
                if (GlobalPeriodComboBox.SelectedItem is not GlobalLeaderboardPeriodOption period)
                {
                    return;
                }

                result = await _globalLeaderboard.BuildAsync(
                    period,
                    requestedCount,
                    forceRefresh: false,
                    CancellationToken.None);
            }

            if (result.Mode == GlobalLeaderboardMode.Current)
            {
                _currentGlobalCache = result;
            }
            else
            {
                _increaseGlobalCache = result;
            }

            ShowGlobalLeaderboardResult(result);
            StatusText.Text = modeOption.Mode == GlobalLeaderboardMode.Current
                ? "当前 Star 总榜获取完成。"
                : "Star 增量总榜统计完成。";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("统计全站总榜", exception);
            var message = AppDiagnostics.GetUserMessage(exception);
            StatusText.Text = message;
            MessageBox.Show(
                this,
                $"{message}\n\n诊断日志：\n{logPath}",
                "总榜统计失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetGlobalBusyState(false);
        }
    }

    private async void SaveGlobalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGlobalLeaderboard is null)
        {
            return;
        }

        try
        {
            SaveGlobalButton.IsEnabled = false;
            var saved = await _library.SaveGlobalLeaderboardReportAsync(_currentGlobalLeaderboard);
            ReportPathText.Text = saved.FilePath;
            ReportPathText.ToolTip = saved.FilePath;
            StatusText.Text = $"总榜报告已保存：{saved.FileName}";
            await RefreshHistoryAsync();
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("保存全站总榜", exception);
            var message = AppDiagnostics.GetUserMessage(exception);
            StatusText.Text = message;
            MessageBox.Show(
                this,
                $"{message}\n\n诊断日志：\n{logPath}",
                "保存总榜失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SaveGlobalButton.IsEnabled = _currentGlobalLeaderboard?.Entries.Count > 0;
        }
    }

    private void GlobalLeaderboardGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedGlobalEntry = GlobalLeaderboardGrid.SelectedItem as GlobalLeaderboardEntry;
        OpenGlobalRepositoryButton.IsEnabled = _selectedGlobalEntry is not null;
        if (_selectedGlobalEntry is null)
        {
            _globalSummaryCancellation?.Cancel();
            GlobalDetailSummaryText.Text = "选择项目后自动读取 README 简介。";
            GlobalDetailLlmText.Text = "导入本地 LLM 包后自动生成。";
            return;
        }

        GlobalDetailNameText.Text = _selectedGlobalEntry.FullName;
        GlobalDetailDescriptionText.Text = string.IsNullOrWhiteSpace(_selectedGlobalEntry.Description)
            ? "无项目描述。"
            : _selectedGlobalEntry.Description;
        LoadGlobalSummary(
            _selectedGlobalEntry.FullName,
            _selectedGlobalEntry.Description,
            []);
        GlobalDetailStatsText.Text = _currentGlobalLeaderboard?.Mode == GlobalLeaderboardMode.Current
            ? $"当前 Star：{_selectedGlobalEntry.CurrentStars:N0}"
            : $"Star 增量：{_selectedGlobalEntry.StarIncrease:+#,##0;-#,##0;0}\n" +
              $"上榜天数：{_selectedGlobalEntry.AppearanceDays:N0}\n" +
              $"首次上榜：{_selectedGlobalEntry.FirstSeenDate:yyyy-MM-dd}\n" +
              $"最近上榜：{_selectedGlobalEntry.LastSeenDate:yyyy-MM-dd}";
        GlobalDetailSourceText.Text =
            $"开源时间：{FormatDate(_selectedGlobalEntry.CreatedAt)}\n" +
            $"GitHub：{_selectedGlobalEntry.HtmlUrl}";
    }

    private async void LoadGlobalSummary(
        string fullName,
        string fallbackDescription,
        IReadOnlyList<string> topics)
    {
        _globalSummaryCancellation?.Cancel();
        _globalSummaryCancellation?.Dispose();
        _globalSummaryCancellation = new CancellationTokenSource();
        var cancellationToken = _globalSummaryCancellation.Token;
        GlobalDetailSummaryText.Text = "正在读取项目 README 简介...";

        try
        {
            var summary = await _gitHub.GetProjectSummaryAsync(
                fullName,
                fallbackDescription,
                cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                GlobalDetailSummaryText.Text = summary;
                await StreamLlmInsightAsync(
                    fullName,
                    fallbackDescription,
                    topics,
                    summary,
                    GlobalDetailLlmText,
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Selection changed before the README request completed.
        }
    }

    private async Task StreamLlmInsightAsync(
        string fullName,
        string description,
        IReadOnlyList<string> topics,
        string readmeSummary,
        TextBlock target,
        CancellationToken cancellationToken)
    {
        if (!_localLlm.IsAvailable)
        {
            target.Text = "导入本地 LLM 包后启用中文总结和翻译。";
            return;
        }

        target.Text = "正在加载本地模型并生成总结与翻译...";
        var builder = new StringBuilder();
        try
        {
            await foreach (var token in _localLlm.GenerateProjectInsightAsync(
                               fullName,
                               description,
                               topics,
                               readmeSummary,
                               cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                builder.Append(token);
                target.Text = builder.ToString();
            }

            if (builder.Length == 0)
            {
                target.Text = "本地模型没有生成有效内容。";
            }
        }
        catch (OperationCanceledException)
        {
            // A different repository was selected.
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("本地 LLM 推理", exception);
            target.Text = $"本地模型运行失败：{AppDiagnostics.GetUserMessage(exception)}";
        }
    }

    private void GlobalLeaderboardGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_selectedGlobalEntry is not null)
        {
            OpenWithShell(_selectedGlobalEntry.HtmlUrl);
        }
    }

    private void OpenGlobalRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGlobalEntry is not null)
        {
            OpenWithShell(_selectedGlobalEntry.HtmlUrl);
        }
    }

    private void GlobalRankingInput_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            var mode = (GlobalModeComboBox.SelectedItem as GlobalLeaderboardModeOption)?.Mode
                       ?? GlobalLeaderboardMode.Increase;
            if (mode == GlobalLeaderboardMode.Current &&
                ReferenceEquals(sender, GlobalCountComboBox))
            {
                ApplyCurrentCountFromSnapshot();
                return;
            }

            SaveGlobalButton.IsEnabled = false;
            if (_currentGlobalLeaderboard is not null)
            {
                GlobalLeaderboardSummaryText.Text = "统计参数已改变，请重新点击“统计总榜”。";
            }
        }
    }

    private void GlobalModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var mode = (GlobalModeComboBox.SelectedItem as GlobalLeaderboardModeOption)?.Mode
                   ?? GlobalLeaderboardMode.Increase;
        // Current Star means "right now", so never restore an older cached result.
        _currentGlobalCache = null;
        _currentStarRepositories = [];
        var cachedResult = mode == GlobalLeaderboardMode.Increase
            ? _increaseGlobalCache
            : null;
        if (cachedResult is not null)
        {
            ShowGlobalLeaderboardResult(cachedResult);
        }
        else
        {
            ClearGlobalLeaderboardView(mode);
        }
    }

    private void UpdateGlobalModeLayout(GlobalLeaderboardMode mode)
    {
        var isCurrent = mode == GlobalLeaderboardMode.Current;
        CollectGlobalButton.Content = isCurrent ? "刷新当前总榜" : "统计增量总榜";
        GlobalStarIncreaseColumn.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        GlobalCurrentStarsColumn.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
        GlobalAppearanceDaysColumn.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        GlobalLastSeenColumn.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        GlobalPeriodLabelText.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        GlobalPeriodComboBox.Visibility = isCurrent ? Visibility.Collapsed : Visibility.Visible;
        GlobalPeriodLabelColumn.Width = isCurrent ? new GridLength(0) : GridLength.Auto;
        GlobalPeriodComboColumn.Width = isCurrent ? new GridLength(0) : new GridLength(200);
        GlobalPeriodComboBox.IsEnabled =
            !isCurrent && CollectGlobalButton.IsEnabled;
    }

    private void ApplyCurrentCountFromSnapshot()
    {
        if (_currentStarRepositories.Count == 0)
        {
            return;
        }

        if (!int.TryParse(GlobalCountComboBox.Text.Trim(), out var requestedCount) ||
            requestedCount is < 1 or > 1000)
        {
            return;
        }

        if (requestedCount > _currentStarRepositories.Count)
        {
            SaveGlobalButton.IsEnabled = false;
            GlobalLeaderboardSummaryText.Text =
                $"当前快照包含 {_currentStarRepositories.Count:N0} 条记录，请点击“刷新当前总榜”获取更多记录。";
            return;
        }

        var result = BuildCurrentLeaderboardResult(
            _currentStarRepositories.Take(requestedCount).ToList());
        ShowGlobalLeaderboardResult(result);
        StatusText.Text = $"已从同一实时快照中显示前 {requestedCount:N0} 名。";
    }

    private void GlobalCountComboBox_LostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        var mode = (GlobalModeComboBox.SelectedItem as GlobalLeaderboardModeOption)?.Mode;
        if (mode == GlobalLeaderboardMode.Current)
        {
            ApplyCurrentCountFromSnapshot();
        }
    }

    private void GlobalCountComboBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        var mode = (GlobalModeComboBox.SelectedItem as GlobalLeaderboardModeOption)?.Mode;
        if (mode == GlobalLeaderboardMode.Current)
        {
            e.Handled = true;
            ApplyCurrentCountFromSnapshot();
        }
    }

    private void ShowGlobalLeaderboardResult(GlobalLeaderboardResult result)
    {
        _currentGlobalLeaderboard = result;
        _selectedGlobalEntry = null;
        GlobalLeaderboardGrid.ItemsSource = result.Entries;
        SaveGlobalButton.IsEnabled = result.Entries.Count > 0;
        UpdateGlobalModeLayout(result.Mode);

        if (result.Mode == GlobalLeaderboardMode.Current)
        {
            GlobalLeaderboardTitleText.Text = "GitHub 当前 Star 总榜";
            GlobalLeaderboardSummaryText.Text =
                $"GitHub 实时数据 · 生成于 {result.GeneratedAt:yyyy-MM-dd HH:mm:ss} · " +
                $"{result.Entries.Count:N0} 条记录";
        }
        else
        {
            GlobalLeaderboardTitleText.Text = $"{result.PeriodTitle} Star 增量总榜";
            GlobalLeaderboardSummaryText.Text =
                $"数据截止 {result.LatestDataDate:yyyy-MM-dd} · " +
                $"统计 {result.StartDataDate:yyyy-MM-dd} 至 {result.LatestDataDate:yyyy-MM-dd} · " +
                $"{result.SourceFileCount:N0} 个日榜文件 · {result.Entries.Count:N0} 条记录";
        }

        GlobalDetailNameText.Text = "选择一个项目查看详情";
        GlobalDetailDescriptionText.Text = result.SourceNote;
        _globalSummaryCancellation?.Cancel();
        GlobalDetailSummaryText.Text = "选择项目后自动读取 README 简介。";
        GlobalDetailLlmText.Text = "导入本地 LLM 包后自动生成。";
        GlobalDetailStatsText.Text = result.Mode == GlobalLeaderboardMode.Current
            ? $"数据源：GitHub Search API\n生成时间：{result.GeneratedAt:yyyy-MM-dd HH:mm:ss}"
            : $"缓存状态：{result.CacheStatus}";
        GlobalDetailSourceText.Text = result.Mode == GlobalLeaderboardMode.Current
            ? "按 GitHub 当前 Star 总量排序。"
            : "说明：项目未进入某日 OpenGithubs 日榜的日期，不会计入该项目的增长。";
        OpenGlobalRepositoryButton.IsEnabled = false;
        if (result.Entries.Count > 0)
        {
            GlobalLeaderboardGrid.SelectedIndex = 0;
        }
    }

    private void ClearGlobalLeaderboardView(GlobalLeaderboardMode mode)
    {
        _currentGlobalLeaderboard = null;
        _selectedGlobalEntry = null;
        GlobalLeaderboardGrid.ItemsSource = null;
        SaveGlobalButton.IsEnabled = false;
        UpdateGlobalModeLayout(mode);
        GlobalLeaderboardTitleText.Text = mode == GlobalLeaderboardMode.Current
            ? "GitHub 当前 Star 总榜"
            : "全站 Star 增量总榜";
        GlobalLeaderboardSummaryText.Text = mode == GlobalLeaderboardMode.Current
            ? "按 GitHub 当前 Star 总量排序，不依赖统计周期。"
            : "选择日、周、月、双月、半年或年，再点击“统计总榜”。";
        GlobalDetailNameText.Text = "选择一个项目查看详情";
        GlobalDetailDescriptionText.Text = "统计后选择项目查看详情。";
        _globalSummaryCancellation?.Cancel();
        GlobalDetailSummaryText.Text = "选择项目后自动读取 README 简介。";
        GlobalDetailLlmText.Text = "导入本地 LLM 包后自动生成。";
        GlobalDetailStatsText.Text = "";
        GlobalDetailSourceText.Text = "";
        OpenGlobalRepositoryButton.IsEnabled = false;
    }

    private static GlobalLeaderboardResult BuildCurrentLeaderboardResult(
        IReadOnlyList<RepositoryRecord> repositories)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new GlobalLeaderboardResult
        {
            Mode = GlobalLeaderboardMode.Current,
            PeriodTitle = "当前 Star",
            PeriodDays = 0,
            LatestDataDate = today,
            StartDataDate = today,
            SourceFileCount = 0,
            CacheStatus = "GitHub Search API 实时查询",
            SourceNote = "数据源：GitHub REST Search API，按当前 stargazers_count 排序。",
            GeneratedAt = DateTimeOffset.Now,
            Entries = repositories
                .OrderByDescending(repository => repository.Stars)
                .ThenBy(repository => repository.FullName, StringComparer.OrdinalIgnoreCase)
                .Select((repository, index) => new GlobalLeaderboardEntry
                {
                    Rank = index + 1,
                    FullName = repository.FullName,
                    HtmlUrl = repository.HtmlUrl,
                    Description = repository.Description,
                    CurrentStars = repository.Stars,
                    StarIncrease = 0,
                    AppearanceDays = 0,
                    FirstSeenDate = today,
                    LastSeenDate = today,
                    CreatedAt = repository.CreatedAt
                }).ToList()
        };
    }

    private bool TryGetCount(string text, string label, out int count)
    {
        if (int.TryParse(text.Trim(), out count) && count is >= 1 and <= 1000)
        {
            return true;
        }

        MessageBox.Show(
            this,
            $"{label}必须是 1 到 1000 之间的整数。",
            "数量无效",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void SetGlobalBusyState(bool busy)
    {
        CollectGlobalButton.IsEnabled = !busy;
        GlobalModeComboBox.IsEnabled = !busy;
        var mode = (GlobalModeComboBox.SelectedItem as GlobalLeaderboardModeOption)?.Mode
                   ?? GlobalLeaderboardMode.Increase;
        GlobalPeriodComboBox.IsEnabled = !busy && mode == GlobalLeaderboardMode.Increase;
        GlobalCountComboBox.IsEnabled = !busy;
        SaveGlobalButton.IsEnabled = !busy && _currentGlobalLeaderboard?.Entries.Count > 0;
        Cursor = busy ? Cursors.Wait : Cursors.Arrow;
    }

    private static void OpenWithShell(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
