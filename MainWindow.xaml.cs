using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using GitHubTopicReports.Models;
using GitHubTopicReports.Services;

namespace GitHubTopicReports;

public partial class MainWindow : Window
{
    private static readonly TimeSpan FavoriteDragArmDelay =
        TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FavoriteOrderAnimationDuration =
        TimeSpan.FromMilliseconds(180);
    private static readonly Regex NoteImageRegex = new(
        @"!\[[^\]]*\]\((?<url>(?:data:image/|images/|Attachments/)[^)\s]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly GitHubSearchService _gitHub;
    private readonly ReportLibraryService _library = new();
    private readonly DispatcherTimer _favoriteDragTimer = new()
    {
        Interval = FavoriteDragArmDelay
    };
    private readonly DispatcherTimer _favoriteFolderDragTimer = new()
    {
        Interval = FavoriteDragArmDelay
    };
    private readonly DispatcherTimer _noteAutoSaveTimer = new()
    {
        Interval = TimeSpan.FromMinutes(3)
    };
    private readonly SemaphoreSlim _noteSaveGate = new(1, 1);
    private readonly FavoritesService _favorites;
    private readonly NotesService _notesService;
    private readonly GlobalLeaderboardService _globalLeaderboard;
    private readonly ConfigurationPackageService _configurationPackages;
    private readonly LlmPackageService _llmPackageService;
    private readonly LlmPromptService _llmPromptService;
    private readonly LocalLlmService _localLlm;
    private readonly AppSettingsService _appSettingsService;
    private AppSettings _appSettings;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private System.Drawing.Icon? _trayIconImage;
    private bool _isExiting;
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
    private bool _favoriteDragArmed;
    private bool _favoriteDragOrderChanged;
    private FavoriteRepository? _favoriteDragRepository;
    private List<FavoriteRepository>? _favoriteDragOriginalItems;
    private Dictionary<long, int>? _favoriteDragOriginalSortOrders;
    private FavoriteDragGhostAdorner? _favoriteDragGhost;
    private AdornerLayer? _favoriteDragAdornerLayer;
    private int _favoriteOrderAnimationVersion;
    private bool _favoriteFolderDragArmed;
    private bool _favoriteFolderDragOrderChanged;
    private ListBox? _favoriteFolderDragList;
    private FavoriteFolder? _favoriteFolderDragFolder;
    private List<FavoriteFolder>? _favoriteFolderDragOriginalItems;
    private Dictionary<string, int>? _favoriteFolderDragOriginalSortOrders;
    private FavoriteDragGhostAdorner? _favoriteFolderDragGhost;
    private AdornerLayer? _favoriteFolderDragAdornerLayer;
    private int _favoriteFolderOrderAnimationVersion;
    private List<NoteDocument> _noteItems = [];
    private List<NoteFolder> _noteFolders = [];
    private ObservableCollection<NoteTreeItem> _noteTreeRoots = [];
    private NoteDocument? _currentNote;
    private NoteCryptoKey? _currentNoteKey;
    private bool _suppressNoteSelection;
    private NoteTreeItem? _noteContextItem;
    private bool _noteTreeDragPending;
    private bool _noteTreeDragging;
    private Point _noteTreeDragStart;
    private NoteTreeItem? _noteTreeDragItem;
    private NoteTreeActionGutterAdorner? _noteTreeActionAdorner;
    private AdornerLayer? _noteTreeActionAdornerLayer;
    private FavoriteDragGhostAdorner? _noteTreeDragGhost;
    private AdornerLayer? _noteTreeDragAdornerLayer;
    private int _noteTreeOrderAnimationVersion;
    private TreeViewItem? _noteTreeDropTarget;
    private NoteDropTargetAdorner? _noteTreeDropHighlight;
    private AdornerLayer? _noteTreeDropHighlightLayer;
    private bool _suppressNoteEditorEvents;
    private bool _noteDirty;
    private int _noteEditVersion;
    private double _noteZoom = 1.0;
    private BitmapSource? _selectedNoteImage;
    private bool _closeAfterNoteSave;
    private bool _closeNoteSaveInProgress;
    private string _currentKeyword = "";

    public MainWindow()
    {
        _favorites = new FavoritesService(_library.LibraryRoot);
        _notesService = new NotesService(_library.LibraryRoot);
        _gitHub = new GitHubSearchService(_library.LibraryRoot);
        _globalLeaderboard = new GlobalLeaderboardService(_library.LibraryRoot);
        _configurationPackages = new ConfigurationPackageService(_library.LibraryRoot);
        _llmPackageService = new LlmPackageService(_library.LibraryRoot);
        _llmPromptService = new LlmPromptService(_library.LibraryRoot);
        _localLlm = new LocalLlmService(_llmPackageService, _llmPromptService);
        _appSettingsService = new AppSettingsService(_library.LibraryRoot);
        try
        {
            _appSettings = _appSettingsService.Load();
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("读取软件设置", exception);
            _appSettings = new AppSettings();
        }
        ThemeManager.ApplySystemTheme();
        InitializeComponent();
        UpdateThemeToggleButton();
        ApplySavedLayout();
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
        CombinedContentTabs.Items.Remove(CombinedDirectoryTab);
        CombinedContentTabs.Items.Insert(0, CombinedDirectoryTab);
        CombinedContentTabs.SelectedItem = CombinedDirectoryTab;
        _favoriteDragTimer.Tick += FavoriteDragTimer_Tick;
        _favoriteFolderDragTimer.Tick += FavoriteFolderDragTimer_Tick;
        _noteAutoSaveTimer.Tick += NoteAutoSaveTimer_Tick;
        NoteTree.ContextMenu = new ContextMenu();
        NoteTree.Loaded += (_, _) =>
        {
            EnsureNoteTreeActionAdorner();
            _noteTreeActionAdorner?.Refresh();
        };
        NoteTree.LayoutUpdated += (_, _) =>
            _noteTreeActionAdorner?.Refresh();
        NoteTree.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler((_, _) =>
                _noteTreeActionAdorner?.Refresh()));
        CommandManager.AddPreviewExecutedHandler(
            NoteBodyEditor,
            NoteBodyTextBox_CommandExecuted);
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        WindowState = _appSettings.WindowMaximized
            ? System.Windows.WindowState.Maximized
            : System.Windows.WindowState.Normal;
        CombineFavoriteWorkspace();
        await RefreshHistoryAsync();
        await RefreshSearchDirectoriesAsync();
        await RefreshLlmStatusAsync();
        var notesLoaded = false;
        try
        {
            await RefreshNotesAsync();
            notesLoaded = true;
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("加载笔记仓库", exception);
            NoteCountText.Text = "读取失败";
            StatusText.Text = "笔记仓库无法读取，已停止写入以保护现有文件。";
            MessageBox.Show(
                this,
                $"{AppDiagnostics.GetUserMessage(exception)}\n\n诊断日志：\n{logPath}",
                "笔记仓库读取失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        if (notesLoaded)
        {
            StatusText.Text = _gitHub.HasToken
                ? "就绪。GitHub 登录凭据已加载。"
                : "就绪。登录 GitHub 后可搜索账号有权访问的私有仓库。";
        }
        StandaloneSearchKeywordTextBox.Focus();
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsNoteSaveShortcut(e.Key, Keyboard.Modifiers) ||
            !ReferenceEquals(WorkspaceTabs.SelectedItem, NotesTab))
        {
            return;
        }

        e.Handled = true;
        await SaveCurrentNoteWithFeedbackAsync();
    }

    private static bool IsNoteSaveShortcut(Key key, ModifierKeys modifiers) =>
        key == Key.S &&
        modifiers.HasFlag(ModifierKeys.Control) &&
        !modifiers.HasFlag(ModifierKeys.Alt);

    private void ApplyResponsiveLayout(double width)
    {
        HeaderDescriptionText.Visibility =
            width < 1100 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        var isDark = ThemeManager.Toggle();
        UpdateThemeToggleButton();
        StatusText.Text = isDark ? "已切换到深色模式。" : "已切换到浅色模式。";
    }

    private void UpdateThemeToggleButton()
    {
        ThemeToggleButton.Content = ThemeManager.IsDark ? "浅色模式" : "深色模式";
        ThemeToggleButton.ToolTip = ThemeManager.IsDark
            ? "切换到浅色模式"
            : "切换到深色模式";
        AutomationProperties.SetName(
            ThemeToggleButton,
            ThemeManager.IsDark ? "切换到浅色模式" : "切换到深色模式");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AppSettingsDialog(_appSettings, _gitHub)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var previousSettings = _appSettings;
        try
        {
            new StartupRegistrationService().SetEnabled(
                dialog.CurrentSettings.StartWithWindows);
            _appSettingsService.Save(dialog.CurrentSettings);
            _appSettings = dialog.CurrentSettings;
            StatusText.Text = "软件设置已保存。";
        }
        catch (Exception exception)
        {
            try
            {
                new StartupRegistrationService().SetEnabled(
                    previousSettings.StartWithWindows);
            }
            catch (Exception rollbackException)
            {
                AppDiagnostics.LogException("恢复开机自启设置", rollbackException);
            }

            var logPath = AppDiagnostics.LogException("保存软件设置", exception);
            MessageBox.Show(
                this,
                $"{AppDiagnostics.GetUserMessage(exception)}\n\n诊断日志：\n{logPath}",
                "设置保存失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (dialog.AccountChanged)
        {
            StatusText.Text = _gitHub.HasToken
                ? "GitHub 账号已登录，可搜索有权限访问的私有仓库。"
                : "GitHub 账号已退出，仅显示公开仓库。";
        }

        switch (dialog.RequestedAction)
        {
            case AppSettingsAction.ImportLlm:
                ImportLlmPackageButton_Click(this, new RoutedEventArgs());
                break;
            case AppSettingsAction.ImportConfiguration:
                ImportConfigurationButton_Click(this, new RoutedEventArgs());
                break;
            case AppSettingsAction.ExportConfiguration:
                ExportConfigurationButton_Click(this, new RoutedEventArgs());
                break;
            case AppSettingsAction.OpenLibrary:
                OpenLibraryButton_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_closeAfterNoteSave && ShouldSaveNoteBeforeClose())
        {
            e.Cancel = true;
            if (_closeNoteSaveInProgress)
            {
                return;
            }

            var choice = MessageBox.Show(
                this,
                $"笔记“{_currentNote!.DisplayTitle}”有尚未保存的修改。\n\n" +
                "选择“是”保存并继续关闭，选择“否”放弃修改并关闭，选择“取消”返回。",
                "笔记尚未保存",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Cancel)
            {
                return;
            }

            if (choice == MessageBoxResult.No)
            {
                _noteDirty = false;
                LockCurrentNote(showCurrentState: false);
                _closeAfterNoteSave = true;
                ContinueClosingAfterNoteDecision();
                return;
            }

            _closeNoteSaveInProgress = true;
            try
            {
                await SaveCurrentNoteAsync(force: true);
                LockCurrentNote(showCurrentState: false);
                _closeAfterNoteSave = true;
                ContinueClosingAfterNoteDecision();
            }
            catch (Exception exception)
            {
                ShowNoteError("关闭前保存笔记失败", exception);
            }
            finally
            {
                _closeNoteSaveInProgress = false;
            }
            return;
        }

        _closeAfterNoteSave = false;
        SaveLayoutSettings();
        if (_isExiting)
        {
            return;
        }

        var action = _appSettings.CloseAction;
        if (_appSettings.AskOnClose)
        {
            var dialog = new CloseBehaviorDialog(_appSettings.AskOnClose)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true || dialog.SelectedAction is not { } selectedAction)
            {
                e.Cancel = true;
                return;
            }

            action = selectedAction;
            var updatedSettings = _appSettings.Copy();
            updatedSettings.CloseAction = selectedAction;
            updatedSettings.AskOnClose = dialog.AskOnClose;
            _appSettings = updatedSettings;
            try
            {
                _appSettingsService.Save(updatedSettings);
            }
            catch (Exception exception)
            {
                AppDiagnostics.LogException("保存关闭窗口设置", exception);
                MessageBox.Show(
                    this,
                    "关闭窗口设置未能保存，但本次操作仍会继续。\n\n" +
                    AppDiagnostics.GetUserMessage(exception),
                    "设置保存失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        if (action == AppCloseAction.Exit)
        {
            _isExiting = true;
            DisposeTrayIcon();
        }
        else
        {
            e.Cancel = true;
            MinimizeToTray();
        }
    }

    private void MinimizeToTray()
    {
        EnsureTrayIcon();
        ShowInTaskbar = false;
        Hide();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
            return;
        }

        var openItem = new System.Windows.Forms.ToolStripMenuItem("打开主窗口");
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("完全退出");
        exitItem.Click += (_, _) => ExitApplication();

        _trayMenu = new System.Windows.Forms.ContextMenuStrip();
        _trayMenu.Items.Add(openItem);
        _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayMenu.Items.Add(exitItem);

        try
        {
            var executablePath = Environment.ProcessPath;
            _trayIconImage = !string.IsNullOrWhiteSpace(executablePath)
                ? System.Drawing.Icon.ExtractAssociatedIcon(executablePath)
                : null;
        }
        catch
        {
            _trayIconImage = null;
        }

        _trayIconImage ??= (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Local GitHub Library",
            Icon = _trayIconImage,
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        DisposeTrayIcon();
        Close();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _trayMenu?.Dispose();
        _trayMenu = null;
        _trayIconImage?.Dispose();
        _trayIconImage = null;
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
        _currentNoteKey?.Dispose();
        _currentNoteKey = null;
        _noteAutoSaveTimer.Stop();
        CommandManager.RemovePreviewExecutedHandler(
            NoteBodyEditor,
            NoteBodyTextBox_CommandExecuted);
        ResetNoteTreeDrag();
        if (_noteTreeActionAdorner is not null &&
            _noteTreeActionAdornerLayer is not null)
        {
            _noteTreeActionAdornerLayer.Remove(_noteTreeActionAdorner);
        }
        _noteTreeActionAdorner = null;
        _noteTreeActionAdornerLayer = null;
        ResetFavoriteDrag();
        ResetFavoriteFolderDrag();
        DisposeTrayIcon();
    }

    private async Task RefreshNotesAsync(
        string? preferredNoteId = null,
        bool activateSelected = true)
    {
        var previousId = _currentNote?.Id;
        _noteItems = (await _notesService.LoadAllAsync()).ToList();
        _noteFolders = (await _notesService.LoadFoldersAsync()).ToList();
        _noteTreeRoots = BuildNoteTree();
        NoteCountText.Text = $"{_noteItems.Count:N0} 篇";

        var selected = _noteItems.FirstOrDefault(note => note.Id == preferredNoteId)
                       ?? _noteItems.FirstOrDefault(note => note.Id == previousId)
                       ?? FirstNoteInTree(_noteTreeRoots);
        _suppressNoteSelection = true;
        try
        {
            NoteTree.ItemsSource = _noteTreeRoots;
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    EnsureNoteTreeActionAdorner();
                    _noteTreeActionAdorner?.Refresh();
                }));
        }
        finally
        {
            _suppressNoteSelection = false;
        }

        if (selected is not null)
        {
            await SelectNoteTreeNodeAsync(selected.Id);
        }
        if (activateSelected)
        {
            await ActivateNoteAsync(selected, saveCurrent: false);
        }
    }

    private async Task SelectNoteTreeNodeAsync(string noteId)
    {
        await Dispatcher.InvokeAsync(
            () =>
            {
                NoteTree.UpdateLayout();
                var container = FindNoteTreeItem(NoteTree, noteId);
                if (container is null)
                {
                    return;
                }

                _suppressNoteSelection = true;
                try
                {
                    container.IsSelected = true;
                    container.BringIntoView();
                }
                finally
                {
                    _suppressNoteSelection = false;
                }
            },
            DispatcherPriority.Loaded);
    }

    private static TreeViewItem? FindNoteTreeItem(ItemsControl parent, string noteId)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item)
                is not TreeViewItem container)
            {
                continue;
            }

            if (item is NoteTreeItem { IsFolder: false, Id: var id } &&
                id == noteId)
            {
                return container;
            }

            container.IsExpanded = true;
            container.UpdateLayout();
            if (FindNoteTreeItem(container, noteId) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    private ObservableCollection<NoteTreeItem> BuildNoteTree()
    {
        var validFolderIds = _noteFolders
            .Select(folder => folder.Id)
            .ToHashSet(StringComparer.Ordinal);
        var visitedFolderIds = new HashSet<string>(StringComparer.Ordinal);

        ObservableCollection<NoteTreeItem> BuildChildren(string? parentId)
        {
            var children = new ObservableCollection<NoteTreeItem>();
            var notes = _noteItems.Where(note =>
                parentId is null
                    ? note.FolderId is null || !validFolderIds.Contains(note.FolderId)
                    : note.FolderId == parentId);
            foreach (var noteItem in notes
                .OrderBy(note => note.SortOrder)
                .ThenByDescending(note => note.UpdatedAt)
                .Select(note => new NoteTreeItem
                {
                    Kind = "note",
                    Id = note.Id,
                    Name = note.DisplayTitle,
                    UpdatedDisplay = note.UpdatedDisplay,
                    IsEncrypted = note.IsEncrypted,
                    FolderId = note.FolderId,
                    Note = note
                }))
            {
                children.Add(noteItem);
            }

            foreach (var folder in _noteFolders
                         .Where(folder => folder.ParentId == parentId)
                         .OrderBy(folder => folder.CreatedAt))
            {
                if (!visitedFolderIds.Add(folder.Id))
                {
                    continue;
                }

                children.Add(new NoteTreeItem
                {
                    Kind = "folder",
                    Id = folder.Id,
                    Name = folder.Name,
                    Children = BuildChildren(folder.Id)
                });
            }

            return children;
        }

        return BuildChildren(null);
    }

    private static NoteDocument? FirstNoteInTree(IEnumerable<NoteTreeItem> items)
    {
        foreach (var item in items)
        {
            if (!item.IsFolder)
            {
                return item.Note;
            }

            if (FirstNoteInTree(item.Children) is { } note)
            {
                return note;
            }
        }

        return null;
    }

    private string? GetSelectedNoteFolderId()
    {
        return NoteTree.SelectedItem switch
        {
            NoteTreeItem { IsFolder: true } folder => folder.Id,
            NoteTreeItem { IsFolder: false } note => note.FolderId,
            _ => null
        };
    }

    private int NextTopNoteSortOrder(string? folderId) =>
        _noteItems
            .Where(note => note.FolderId == folderId)
            .Select(note => note.SortOrder)
            .DefaultIfEmpty(int.MaxValue)
            .Min() - 1;

    private List<NoteFolderOption> GetNoteFolderOptions()
    {
        var options = new List<NoteFolderOption>
        {
            new() { Id = null, DisplayName = "笔记根目录" }
        };

        void AddChildren(string? parentId, int depth)
        {
            foreach (var folder in _noteFolders
                         .Where(folder => folder.ParentId == parentId)
                         .OrderBy(folder => folder.CreatedAt))
            {
                options.Add(new NoteFolderOption
                {
                    Id = folder.Id,
                    DisplayName = $"{new string('　', depth)}{folder.Name}"
                });
                AddChildren(folder.Id, depth + 1);
            }
        }

        AddChildren(null, 0);
        return options;
    }

    private async void CreateNoteFolderButton_Click(object sender, RoutedEventArgs e)
    {
        await CreateNoteFolderAsync(GetSelectedNoteFolderId());
    }

    private async Task CreateNoteFolderAsync(string? parentId)
    {
        var dialog = new FolderNameDialog(
            "新建笔记目录",
            string.IsNullOrWhiteSpace(parentId)
                ? "输入目录名称，将创建在笔记根目录下。"
                : "输入子目录名称，将创建在所选目录下。")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _notesService.CreateFolderAsync(dialog.FolderName, parentId);
            await RefreshNotesAsync(_currentNote?.Id, activateSelected: false);
            StatusText.Text = $"已新建笔记目录“{dialog.FolderName}”。";
        }
        catch (Exception exception)
        {
            ShowNoteError("新建笔记目录失败", exception);
        }
    }

    private async Task MoveNoteToFolderAsync(string noteId, string? folderId)
    {
        var note = _noteItems.FirstOrDefault(item => item.Id == noteId);
        if (note is null || note.FolderId == folderId)
        {
            return;
        }

        try
        {
            await SaveCurrentNoteAsync(force: true);
            note.FolderId = folderId;
            note.SortOrder = NextTopNoteSortOrder(folderId);
            await _notesService.SaveNoteOrderAsync(_noteItems);
            if (_currentNote?.Id == note.Id)
            {
                _currentNote.FolderId = folderId;
            }

            await RefreshNotesAsync(note.Id, activateSelected: false);
            StatusText.Text = "笔记已移动到新目录。";
        }
        catch (Exception exception)
        {
            ShowNoteError("移动笔记失败", exception);
        }
    }

    private async void CreateNoteButton_Click(object sender, RoutedEventArgs e)
    {
        await CreateNoteInFolderAsync(GetSelectedNoteFolderId());
    }

    private async Task CreateNoteInFolderAsync(string? folderId)
    {
        try
        {
            await SaveCurrentNoteAsync(force: true);
            var note = await _notesService.CreateAsync(
                folderId,
                NextTopNoteSortOrder(folderId));
            await RefreshNotesAsync(note.Id);
            NoteTitleTextBox.Focus();
            NoteTitleTextBox.SelectAll();
            StatusText.Text = "已新建笔记。";
        }
        catch (Exception exception)
        {
            ShowNoteError("新建笔记失败", exception);
        }
    }

    private async void NoteTree_SelectedItemChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressNoteSelection)
        {
            return;
        }

        if (e.NewValue is NoteTreeItem { IsFolder: false, Note: { } note })
        {
            await ActivateNoteAsync(note);
        }
        else if (e.NewValue is NoteTreeItem { IsFolder: true } folder)
        {
            StatusText.Text = $"已选择笔记目录“{folder.Name}”，新建的笔记会放入该目录。";
        }
    }

    private void NoteTree_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _noteContextItem = null;
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var container = FindVisualAncestor<TreeViewItem>(source);
        if (container?.DataContext is not NoteTreeItem item)
        {
            return;
        }

        _noteContextItem = item;
        container.IsSelected = true;
    }

    private void NoteTree_ContextMenuOpening(
        object sender,
        ContextMenuEventArgs e)
    {
        var item = _noteContextItem ?? NoteTree.SelectedItem as NoteTreeItem;
        if (item is null)
        {
            e.Handled = true;
            return;
        }

        var menu = NoteTree.ContextMenu ?? new ContextMenu();
        menu.Items.Clear();
        if (item.IsFolder)
        {
            menu.Items.Add(CreateNoteContextMenuItem(
                "在此目录新建笔记",
                async () =>
                {
                    await CreateNoteInFolderAsync(item.Id);
                }));
            menu.Items.Add(CreateNoteContextMenuItem(
                "新建子目录",
                async () =>
                {
                    await CreateNoteFolderAsync(item.Id);
                }));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateNoteContextMenuItem(
                "删除此目录",
                async () =>
                {
                    await DeleteFolderByIdAsync(item.Id);
                }));
        }
        else if (item.Note is { } note)
        {
            menu.Items.Add(CreateNoteContextMenuItem(
                "打开笔记",
                async () =>
                {
                    await ActivateNoteAsync(note);
                }));
            var openWithMenu = new MenuItem { Header = "打开方式" };
            openWithMenu.Items.Add(CreateNoteContextMenuItem(
                "默认程序",
                () =>
                {
                    OpenNoteFile(note.Id);
                    return Task.CompletedTask;
                }));
            openWithMenu.Items.Add(CreateNoteContextMenuItem(
                "记事本",
                () =>
                {
                    OpenNoteFileWith(note.Id, "notepad.exe");
                    return Task.CompletedTask;
                }));
            openWithMenu.Items.Add(CreateNoteContextMenuItem(
                "Visual Studio Code",
                () =>
                {
                    OpenNoteFileWith(note.Id, "code");
                    return Task.CompletedTask;
                }));
            openWithMenu.Items.Add(new Separator());
            openWithMenu.Items.Add(CreateNoteContextMenuItem(
                "在资源管理器中显示",
                () =>
                {
                    ShowNoteFileInExplorer(note.Id);
                    return Task.CompletedTask;
                }));
            menu.Items.Add(openWithMenu);
            if (_currentNote?.Id == note.Id)
            {
                menu.Items.Add(CreateNoteContextMenuItem(
                    "保存笔记",
                    SaveCurrentNoteWithFeedbackAsync));
            }

            var moveMenu = new MenuItem { Header = "移动到目录" };
            foreach (var option in GetNoteFolderOptions())
            {
                var menuItem = new MenuItem
                {
                    Header = option.DisplayName,
                    IsCheckable = true,
                    IsChecked = option.Id == note.FolderId
                };
                menuItem.Click += async (_, _) =>
                {
                    await MoveNoteToFolderAsync(note.Id, option.Id);
                };
                moveMenu.Items.Add(menuItem);
            }
            menu.Items.Add(moveMenu);
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateNoteContextMenuItem(
                "删除笔记",
                async () =>
                {
                    await DeleteNoteByIdAsync(note.Id, note.DisplayTitle);
                }));
        }

        NoteTree.ContextMenu = menu;
    }

    private void OpenNoteFile(string noteId)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _notesService.GetNotePath(noteId),
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowNoteError("打开笔记文件失败", exception);
        }
    }

    private void OpenNoteFileWith(string noteId, string executable)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"\"{_notesService.GetNotePath(noteId)}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowNoteError($"使用 {executable} 打开笔记失败", exception);
        }
    }

    private void ShowNoteFileInExplorer(string noteId)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_notesService.GetNotePath(noteId)}\"",
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowNoteError("在资源管理器中显示笔记失败", exception);
        }
    }

    private static MenuItem CreateNoteContextMenuItem(
        string header,
        Func<Task> action)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) => await action();
        return item;
    }

    private void EnsureNoteTreeActionAdorner()
    {
        if (_noteTreeActionAdorner is not null)
        {
            return;
        }

        _noteTreeActionAdornerLayer = AdornerLayer.GetAdornerLayer(NoteTree);
        if (_noteTreeActionAdornerLayer is null)
        {
            return;
        }

        _noteTreeActionAdorner = new NoteTreeActionGutterAdorner(NoteTree);
        _noteTreeActionAdornerLayer.Add(_noteTreeActionAdorner);
    }

    private bool TryGetNoteTreeActionTarget(
        Point point,
        out TreeViewItem container,
        out NoteTreeItem item)
    {
        container = null!;
        item = null!;
        if (_noteTreeActionAdorner is null ||
            Math.Abs(point.X - _noteTreeActionAdorner.ActionCenterX) > 15)
        {
            return false;
        }

        if (_noteTreeActionAdorner.FindContainerAt(point.Y)
            is not { DataContext: NoteTreeItem candidate } target)
        {
            return false;
        }

        container = target;
        item = candidate;
        return true;
    }

    private async void NoteTree_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        _noteContextItem = null;
        ResetNoteTreeDrag();
        if (e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (TryGetNoteTreeActionTarget(
                e.GetPosition(NoteTree),
                out var actionContainer,
                out var actionItem))
        {
            e.Handled = true;
            actionContainer.IsSelected = true;
            if (actionItem.IsFolder)
            {
                await DeleteFolderByIdAsync(actionItem.Id);
            }
            else if (actionItem.Note is { } note)
            {
                await DeleteNoteByIdAsync(note.Id, note.DisplayTitle);
            }
            return;
        }

        if (FindVisualAncestor<Button>(source) is not null ||
            FindVisualAncestor<ToggleButton>(source) is not null)
        {
            return;
        }

        var container = FindVisualAncestor<TreeViewItem>(source);
        if (container?.DataContext is not NoteTreeItem { IsFolder: false } noteItem)
        {
            return;
        }

        _noteTreeDragStart = e.GetPosition(NoteTree);
        _noteTreeDragItem = noteItem;
        _noteTreeDragPending = true;
    }

    private void NoteTree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_noteTreeDragPending && !_noteTreeDragging)
        {
            NoteTree.Cursor = TryGetNoteTreeActionTarget(
                e.GetPosition(NoteTree),
                out _,
                out _)
                ? Cursors.Hand
                : Cursors.Arrow;
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ResetNoteTreeDrag();
            return;
        }

        var position = e.GetPosition(NoteTree);
        if (!_noteTreeDragging)
        {
            var delta = position - _noteTreeDragStart;
            if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6)
            {
                return;
            }

            _noteTreeDragging = true;
            NoteTree.CaptureMouse();
            NoteTree.Cursor = Cursors.SizeAll;
        }

        ShowNoteTreeDragGhost(position);
        UpdateNoteTreeDropHighlight(position);
        ReorderNoteAtPoint(position);
    }

    private void NoteTree_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_noteTreeDragging)
        {
            NoteTree.Cursor = Cursors.Arrow;
        }
    }

    private async void NoteTree_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!_noteTreeDragging || _noteTreeDragItem is null)
        {
            ResetNoteTreeDrag();
            return;
        }

        var source = _noteTreeDragItem;
        var pointer = e.GetPosition(NoteTree);
        TreeViewItem? targetContainer = null;
        if (NoteTree.InputHitTest(pointer) is DependencyObject hit)
        {
            targetContainer = FindVisualAncestor<TreeViewItem>(hit);
        }

        ResetNoteTreeDrag();
        await MoveNoteByDropAsync(source, targetContainer, pointer.Y);
    }

    private async Task MoveNoteByDropAsync(
        NoteTreeItem sourceItem,
        TreeViewItem? targetContainer,
        double pointerY)
    {
        if (sourceItem.Note is null)
        {
            return;
        }

        var note = _noteItems.FirstOrDefault(item => item.Id == sourceItem.Id);
        if (note is null)
        {
            return;
        }

        string? targetFolderId = null;
        NoteTreeItem? targetNoteItem = null;
        var dropBelow = true;
        if (targetContainer?.DataContext is NoteTreeItem targetItem)
        {
            if (targetItem.IsFolder)
            {
                targetFolderId = targetItem.Id;
                dropBelow = false;
            }
            else
            {
                targetFolderId = targetItem.FolderId;
                targetNoteItem = targetItem;
                var top = targetContainer.TranslatePoint(
                    new Point(0, 0),
                    NoteTree).Y;
                dropBelow = pointerY > top + targetContainer.ActualHeight / 2;
            }
        }

        var oldFolderId = note.FolderId;
        note.FolderId = targetFolderId;
        var targetNotes = _noteItems
            .Where(item =>
                item.Id != note.Id &&
                item.FolderId == targetFolderId)
            .OrderBy(item => item.SortOrder)
            .ThenByDescending(item => item.UpdatedAt)
            .ToList();
        var insertIndex = targetNotes.Count;
        if (targetNoteItem is not null)
        {
            var targetIndex = targetNotes.FindIndex(item =>
                item.Id == targetNoteItem.Id);
            if (targetIndex >= 0)
            {
                insertIndex = targetIndex + (dropBelow ? 1 : 0);
            }
        }
        else if (targetFolderId is not null)
        {
            insertIndex = 0;
        }

        targetNotes.Insert(insertIndex, note);
        for (var index = 0; index < targetNotes.Count; index++)
        {
            targetNotes[index].SortOrder = index;
        }

        if (oldFolderId != targetFolderId)
        {
            var oldFolderNotes = _noteItems
                .Where(item =>
                    item.Id != note.Id &&
                    item.FolderId == oldFolderId)
                .OrderBy(item => item.SortOrder)
                .ThenByDescending(item => item.UpdatedAt)
                .ToList();
            for (var index = 0; index < oldFolderNotes.Count; index++)
            {
                oldFolderNotes[index].SortOrder = index;
            }
        }

        try
        {
            await _notesService.SaveNoteOrderAsync(_noteItems);
            if (_currentNote?.Id == note.Id)
            {
                _currentNote.FolderId = targetFolderId;
            }

            await RefreshNotesAsync(
                _currentNote?.Id,
                activateSelected: false);
            StatusText.Text = targetFolderId is null
                ? "笔记已移动到根目录并调整顺序。"
                : "笔记位置和顺序已更新。";
        }
        catch (Exception exception)
        {
            ShowNoteError("移动笔记失败", exception);
        }
    }

    private void ShowNoteTreeDragGhost(Point position)
    {
        if (_noteTreeDragItem is null)
        {
            return;
        }

        if (_noteTreeDragGhost is null)
        {
            _noteTreeDragAdornerLayer = AdornerLayer.GetAdornerLayer(NoteTree);
            if (_noteTreeDragAdornerLayer is null)
            {
                return;
            }

            _noteTreeDragGhost = new FavoriteDragGhostAdorner(
                NoteTree,
                _noteTreeDragItem.Name,
                "拖动到目录或笔记以调整位置",
                Math.Clamp(NoteTree.ActualWidth - 16, 180, 300),
                centerOnPointer: true);
            _noteTreeDragAdornerLayer.Add(_noteTreeDragGhost);
        }

        _noteTreeDragGhost.UpdatePosition(position);
    }

    private void UpdateNoteTreeDropHighlight(Point position)
    {
        TreeViewItem? target = null;
        if (NoteTree.InputHitTest(position) is DependencyObject hit)
        {
            target = FindVisualAncestor<TreeViewItem>(hit);
        }

        ShowNoteTreeDropHighlight(GetNoteTreeDropHighlightTarget(target));
    }

    private void ReorderNoteAtPoint(Point position)
    {
        if (_noteTreeDragItem is null ||
            NoteTree.InputHitTest(position) is not DependencyObject hit)
        {
            return;
        }

        var targetContainer = FindVisualAncestor<TreeViewItem>(hit);
        if (targetContainer?.DataContext is NoteTreeItem { IsFolder: false })
        {
            ReorderNoteRelativeTo(targetContainer, position.Y);
        }
    }

    private void ReorderNoteRelativeTo(
        TreeViewItem targetContainer,
        double pointerY)
    {
        if (_noteTreeDragItem is null ||
            targetContainer.DataContext is not NoteTreeItem
            {
                IsFolder: false,
                Id: var targetId
            } ||
            targetId == _noteTreeDragItem.Id)
        {
            return;
        }

        var sourceParent = FindNoteTreeParent(_noteTreeDragItem.Id);
        var targetParent = FindNoteTreeParent(targetId);
        if (sourceParent is null ||
            targetParent is null ||
            !ReferenceEquals(sourceParent, targetParent))
        {
            return;
        }

        var sourceIndex = IndexOf(sourceParent, _noteTreeDragItem.Id);
        var targetIndex = IndexOf(sourceParent, targetId);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var dropBelow =
            pointerY -
            targetContainer.TranslatePoint(new Point(0, 0), NoteTree).Y >
            targetContainer.ActualHeight / 2;
        var insertIndex = targetIndex + (dropBelow ? 1 : 0);
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (sourceIndex == insertIndex)
        {
            return;
        }

        var previousPositions = CaptureNoteTreeItemPositions();
        var movedItem = sourceParent[sourceIndex];
        sourceParent.RemoveAt(sourceIndex);
        sourceParent.Insert(insertIndex, movedItem);
        SyncNoteSortOrdersFromTree();
        AnimateNoteTreeItems(previousPositions);

        static int IndexOf(IList<NoteTreeItem> items, string id)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (items[index].Id == id)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    private IList<NoteTreeItem>? FindNoteTreeParent(string itemId)
    {
        if (NoteTree.ItemsSource is IList<NoteTreeItem> roots &&
            roots.Any(item => item.Id == itemId))
        {
            return roots;
        }

        IList<NoteTreeItem>? FindInChildren(IEnumerable<NoteTreeItem> items)
        {
            foreach (var item in items)
            {
                if (item.Children.Any(child => child.Id == itemId))
                {
                    return item.Children;
                }

                if (FindInChildren(item.Children) is { } nested)
                {
                    return nested;
                }
            }

            return null;
        }

        return NoteTree.ItemsSource is IEnumerable<NoteTreeItem> sourceItems
            ? FindInChildren(sourceItems)
            : null;
    }

    private void SyncNoteSortOrdersFromTree()
    {
        if (NoteTree.ItemsSource is not IEnumerable<NoteTreeItem> roots)
        {
            return;
        }

        void Apply(IEnumerable<NoteTreeItem> items)
        {
            var noteIndex = 0;
            foreach (var item in items)
            {
                if (item.IsFolder)
                {
                    Apply(item.Children);
                }
                else if (item.Note is { } note)
                {
                    note.SortOrder = noteIndex++;
                }
            }
        }

        Apply(roots);
    }

    private Dictionary<string, double> CaptureNoteTreeItemPositions()
    {
        var positions = new Dictionary<string, double>(StringComparer.Ordinal);

        void Capture(ItemsControl parent, IEnumerable<NoteTreeItem> items)
        {
            foreach (var item in items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item)
                    is not TreeViewItem container)
                {
                    continue;
                }

                positions[item.Id] = container.TranslatePoint(
                    new Point(0, 0),
                    NoteTree).Y;
                Capture(container, item.Children);
            }
        }

        if (NoteTree.ItemsSource is IEnumerable<NoteTreeItem> roots)
        {
            Capture(NoteTree, roots);
        }

        return positions;
    }

    private void AnimateNoteTreeItems(
        IReadOnlyDictionary<string, double> previousPositions)
    {
        var animationVersion = ++_noteTreeOrderAnimationVersion;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                NoteTree.UpdateLayout();
                AnimateItems(NoteTree, NoteTree.ItemsSource);
                return;

                void AnimateItems(ItemsControl parent, IEnumerable? items)
                {
                    if (items is not IEnumerable<NoteTreeItem> noteItems)
                    {
                        return;
                    }

                    foreach (var item in noteItems)
                    {
                        if (parent.ItemContainerGenerator.ContainerFromItem(item)
                            is not TreeViewItem container)
                        {
                            continue;
                        }

                        if (previousPositions.TryGetValue(item.Id, out var previousY))
                        {
                            var currentY = container.TranslatePoint(
                                new Point(0, 0),
                                NoteTree).Y;
                            var offset = previousY - currentY;
                            if (Math.Abs(offset) >= 0.5)
                            {
                                var transform = new TranslateTransform(0, offset);
                                container.RenderTransform = transform;
                                var animation = new DoubleAnimation(
                                    offset,
                                    0,
                                    FavoriteOrderAnimationDuration)
                                {
                                    EasingFunction = new CubicEase
                                    {
                                        EasingMode = EasingMode.EaseOut
                                    },
                                    FillBehavior = FillBehavior.HoldEnd
                                };
                                animation.Completed += (_, _) =>
                                {
                                    if (animationVersion !=
                                        _noteTreeOrderAnimationVersion)
                                    {
                                        return;
                                    }

                                    transform.Y = 0;
                                    container.ClearValue(RenderTransformProperty);
                                };
                                transform.BeginAnimation(
                                    TranslateTransform.YProperty,
                                    animation);
                            }
                        }

                        AnimateItems(container, item.Children);
                    }
                }
            }));
    }

    private TreeViewItem? GetNoteTreeDropHighlightTarget(TreeViewItem? target)
    {
        if (target?.DataContext is not NoteTreeItem targetItem ||
            targetItem.Id == _noteTreeDragItem?.Id)
        {
            return null;
        }

        if (!targetItem.IsFolder &&
            ItemsControl.ItemsControlFromItemContainer(target) is TreeViewItem parentFolder)
        {
            return parentFolder;
        }

        return target;
    }

    private void ShowNoteTreeDropHighlight(TreeViewItem? highlightTarget)
    {
        if (highlightTarget is null)
        {
            ClearNoteTreeDropHighlight();
            return;
        }

        if (ReferenceEquals(highlightTarget, _noteTreeDropTarget))
        {
            return;
        }

        ClearNoteTreeDropHighlight();
        var layer = AdornerLayer.GetAdornerLayer(highlightTarget);
        if (layer is null)
        {
            return;
        }

        _noteTreeDropTarget = highlightTarget;
        _noteTreeDropHighlightLayer = layer;
        _noteTreeDropHighlight = new NoteDropTargetAdorner(highlightTarget);
        layer.Add(_noteTreeDropHighlight);
    }

    private void ClearNoteTreeDropHighlight()
    {
        if (_noteTreeDropHighlight is not null &&
            _noteTreeDropHighlightLayer is not null)
        {
            _noteTreeDropHighlightLayer.Remove(_noteTreeDropHighlight);
        }

        _noteTreeDropTarget = null;
        _noteTreeDropHighlight = null;
        _noteTreeDropHighlightLayer = null;
    }

    private void ResetNoteTreeDrag()
    {
        ClearNoteTreeDropHighlight();
        if (NoteTree.IsMouseCaptured)
        {
            NoteTree.ReleaseMouseCapture();
        }

        _noteTreeDragPending = false;
        _noteTreeDragging = false;
        _noteTreeDragItem = null;
        NoteTree.Cursor = Cursors.Arrow;
        if (_noteTreeDragGhost is not null &&
            _noteTreeDragAdornerLayer is not null)
        {
            _noteTreeDragAdornerLayer.Remove(_noteTreeDragGhost);
        }
        _noteTreeDragGhost = null;
        _noteTreeDragAdornerLayer = null;
    }

    private async Task ActivateNoteAsync(
        NoteDocument? selected,
        bool saveCurrent = true)
    {
        _noteAutoSaveTimer.Stop();
        if (saveCurrent)
        {
            await SaveCurrentNoteAsync(force: true);
        }

        LockCurrentNote(showCurrentState: false);
        if (selected is null)
        {
            _currentNote = null;
            ShowEmptyNoteEditor();
            return;
        }

        try
        {
            _currentNote = await _notesService.LoadAsync(selected.Id);
            _noteDirty = false;
            _noteEditVersion = 0;
            if (_currentNote.IsEncrypted)
            {
                SetNoteText("", "");
                ShowEncryptedNoteLocked();
            }
            else
            {
                SetNoteText(
                    _currentNote.Title,
                    _currentNote.Content,
                    _currentNote.RichTextData);
                ShowPlainNoteEditor();
            }
        }
        catch (Exception exception)
        {
            _currentNote = null;
            ShowEmptyNoteEditor();
            ShowNoteError("打开笔记失败", exception);
        }

    }

    private void ShowPlainNoteEditor()
    {
        NoteEmptyOverlay.Visibility = Visibility.Collapsed;
        NoteLockedOverlay.Visibility = Visibility.Collapsed;
        NoteTitleTextBox.IsEnabled = true;
        NoteBodyEditor.IsEnabled = true;
        EncryptNoteButton.Visibility = Visibility.Visible;
        EncryptNoteButton.IsEnabled = true;
        LockNoteButton.Visibility = Visibility.Collapsed;
        DeleteNoteButton.IsEnabled = true;
        NoteEncryptionStateText.Text = "未加密笔记";
        NoteSaveStateText.Text = "已保存";
    }

    private void ShowEncryptedNoteLocked()
    {
        NoteEmptyOverlay.Visibility = Visibility.Collapsed;
        NoteLockedOverlay.Visibility = Visibility.Visible;
        NoteTitleTextBox.IsEnabled = false;
        NoteBodyEditor.IsEnabled = false;
        EncryptNoteButton.Visibility = Visibility.Collapsed;
        LockNoteButton.Visibility = Visibility.Collapsed;
        DeleteNoteButton.IsEnabled = true;
        NoteEncryptionStateText.Text = "加密笔记 · 已锁定";
        NoteSaveStateText.Text = "需要密码";
    }

    private void ShowEncryptedNoteUnlocked()
    {
        NoteEmptyOverlay.Visibility = Visibility.Collapsed;
        NoteLockedOverlay.Visibility = Visibility.Collapsed;
        NoteTitleTextBox.IsEnabled = true;
        NoteBodyEditor.IsEnabled = true;
        EncryptNoteButton.Visibility = Visibility.Collapsed;
        LockNoteButton.Visibility = Visibility.Visible;
        LockNoteButton.IsEnabled = true;
        DeleteNoteButton.IsEnabled = true;
        NoteEncryptionStateText.Text = "加密笔记 · 已解锁";
        NoteSaveStateText.Text = "已保存";
    }

    private void ShowEmptyNoteEditor()
    {
        SetNoteText("", "");
        NoteEmptyOverlay.Visibility = Visibility.Visible;
        NoteLockedOverlay.Visibility = Visibility.Collapsed;
        NoteTitleTextBox.IsEnabled = false;
        NoteBodyEditor.IsEnabled = false;
        EncryptNoteButton.Visibility = Visibility.Visible;
        EncryptNoteButton.IsEnabled = false;
        LockNoteButton.Visibility = Visibility.Collapsed;
        DeleteNoteButton.IsEnabled = false;
        NoteEncryptionStateText.Text = "选择或新建一篇笔记。";
        NoteSaveStateText.Text = "";
        NoteCharacterCountText.Text = "0 字符";
    }

    private async Task SaveCurrentNoteWithFeedbackAsync()
    {
        if (_currentNote is null)
        {
            return;
        }

        if (_currentNote.IsEncrypted && _currentNoteKey is null)
        {
            StatusText.Text = "这篇加密笔记已锁定，请先输入密码解锁。";
            return;
        }

        try
        {
            _noteAutoSaveTimer.Stop();
            await SaveCurrentNoteAsync(force: true);
            StatusText.Text = "笔记已保存。";
        }
        catch (Exception exception)
        {
            ShowNoteError("保存笔记失败", exception);
        }
    }

    private async void EncryptNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote is null || _currentNote.IsEncrypted)
        {
            return;
        }

        var dialog = new NotePasswordDialog(
            "加密笔记",
            "设置后，必须输入该密码才能打开这篇笔记。密码无法找回。",
            requireConfirmation: true,
            confirmText: "加密并锁定")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var content = new NoteContent(
                NoteTitleTextBox.Text,
                SerializeNoteBody(),
                SerializeNoteBodyRichText());
            using var key = NoteEncryptionService.CreateKey(dialog.Password);
            await _notesService.SaveEncryptedAsync(_currentNote, content, key);
            _noteDirty = false;
            var noteId = _currentNote.Id;
            LockCurrentNote(showCurrentState: false);
            await RefreshNotesAsync(noteId);
            StatusText.Text = "笔记已加密并锁定。";
        }
        catch (Exception exception)
        {
            ShowNoteError("加密笔记失败", exception);
        }
    }

    private async void UnlockNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote?.Encrypted is null)
        {
            return;
        }

        var dialog = new NotePasswordDialog(
            "解锁笔记",
            "输入这篇加密笔记的密码。",
            requireConfirmation: false)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        NoteCryptoKey? key = null;
        try
        {
            if (_currentNote is null)
            {
                return;
            }

            var opened = await _notesService.OpenEncryptedAsync(
                _currentNote.Id,
                dialog.Password);
            key = opened.Key;
            var content = opened.Content;
            _currentNoteKey?.Dispose();
            _currentNoteKey = key;
            key = null;
            _currentNote.Title = content.Title;
            _currentNote.Content = content.Content;
            _currentNote.RichTextData = content.RichTextData;
            _noteDirty = false;
            SetNoteText(content.Title, content.Content, content.RichTextData);
            ShowEncryptedNoteUnlocked();
        }
        catch (NotePasswordException)
        {
            MessageBox.Show(
                this,
                "密码错误，或这篇笔记的数据已经损坏。",
                "无法解锁",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            ShowNoteError("解锁笔记失败", exception);
        }
        finally
        {
            key?.Dispose();
        }
    }

    private async void LockNoteButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveCurrentNoteAsync(force: true);
            LockCurrentNote(showCurrentState: true);
            StatusText.Text = "笔记已锁定。";
        }
        catch (Exception exception)
        {
            ShowNoteError("锁定笔记失败", exception);
        }
    }

    private async void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentNote is null)
        {
            return;
        }

        await DeleteNoteByIdAsync(_currentNote.Id, _currentNote.DisplayTitle);
    }

    private async Task DeleteNoteByIdAsync(string noteId, string displayTitle)
    {
        var note = _noteItems.FirstOrDefault(item => item.Id == noteId);
        if (note is null)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"确定删除笔记“{displayTitle}”吗？此操作无法撤销。",
            "删除笔记",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        if (note.IsEncrypted &&
            !await VerifyEncryptedNotePasswordForDeletionAsync(note))
        {
            return;
        }

        try
        {
            if (_currentNote?.Id == noteId)
            {
                LockCurrentNote(showCurrentState: false);
            }

            await _notesService.DeleteAsync(noteId);
            await RefreshNotesAsync(
                _currentNote?.Id,
                activateSelected: _currentNote is null);
            StatusText.Text = "笔记已删除。";
        }
        catch (Exception exception)
        {
            ShowNoteError("删除笔记失败", exception);
        }
    }

    private async Task<bool> VerifyEncryptedNotePasswordForDeletionAsync(
        NoteDocument note)
    {
        if (note.Encrypted is null)
        {
            return true;
        }

        var dialog = new NotePasswordDialog(
            "删除加密笔记",
            "这是一篇加密笔记。请输入密码，验证通过后才会删除。",
            requireConfirmation: false,
            confirmText: "验证并删除")
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        try
        {
            var opened = await _notesService.OpenEncryptedAsync(
                note.Id,
                dialog.Password);
            opened.Key.Dispose();
            return true;
        }
        catch (NotePasswordException)
        {
            MessageBox.Show(
                this,
                "密码错误，已取消删除。",
                "无法删除加密笔记",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
        catch (Exception exception)
        {
            ShowNoteError("验证加密笔记失败", exception);
            return false;
        }
    }

    private async Task DeleteFolderByIdAsync(string folderId)
    {
        var folder = _noteFolders.FirstOrDefault(item => item.Id == folderId);
        if (folder is null)
        {
            return;
        }

        var folderIds = GetNoteFolderSubtreeIds(folderId);
        var affectedNotes = _noteItems
            .Where(note =>
                note.FolderId is not null &&
                folderIds.Contains(note.FolderId))
            .ToList();
        var parentName = folder.ParentId is null
            ? "笔记根目录"
            : _noteFolders.FirstOrDefault(item => item.Id == folder.ParentId)?.Name
              ?? "上级目录";
        var detail = affectedNotes.Count == 0
            ? "目录内没有笔记。"
            : $"目录内 {affectedNotes.Count:N0} 篇笔记将移动到“{parentName}”，不会被删除。";
        var confirmation = MessageBox.Show(
            this,
            $"确定删除目录“{folder.Name}”及其子目录吗？\n\n{detail}",
            "删除笔记目录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await SaveCurrentNoteAsync(force: true);
            var nextSortOrder = _noteItems
                .Where(note =>
                    note.FolderId == folder.ParentId &&
                    !folderIds.Contains(note.FolderId ?? ""))
                .Select(note => note.SortOrder)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            foreach (var note in affectedNotes)
            {
                note.FolderId = folder.ParentId;
                note.SortOrder = nextSortOrder++;
            }
            if (_currentNote is not null &&
                affectedNotes.Any(note => note.Id == _currentNote.Id))
            {
                _currentNote.FolderId = folder.ParentId;
            }

            if (affectedNotes.Count > 0)
            {
                await _notesService.SaveNoteOrderAsync(_noteItems);
            }
            await _notesService.DeleteFolderAsync(folderId);
            await RefreshNotesAsync(_currentNote?.Id, activateSelected: false);
            StatusText.Text =
                $"已删除笔记目录“{folder.Name}”，其中的笔记已移动到“{parentName}”。";
        }
        catch (Exception exception)
        {
            ShowNoteError("删除笔记目录失败", exception);
        }
    }

    private HashSet<string> GetNoteFolderSubtreeIds(string folderId)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { folderId };
        var added = true;
        while (added)
        {
            added = false;
            foreach (var folder in _noteFolders)
            {
                if (folder.ParentId is not null &&
                    ids.Contains(folder.ParentId) &&
                    ids.Add(folder.Id))
                {
                    added = true;
                }
            }
        }

        return ids;
    }

    private void NoteEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressNoteEditorEvents)
        {
            return;
        }

        if (NoteCharacterCountText is null || NoteBodyEditor is null)
        {
            return;
        }

        if (ReferenceEquals(sender, NoteBodyEditor))
        {
            _selectedNoteImage = null;
        }

        NoteCharacterCountText.Text =
            $"{GetNoteBodyPlainText().Length:N0} 字符";
        if (_currentNote is null ||
            (_currentNote.IsEncrypted && _currentNoteKey is null))
        {
            return;
        }

        MarkCurrentNoteDirty();
    }

    private void MarkCurrentNoteDirty()
    {
        _noteDirty = true;
        _noteEditVersion++;
        NoteSaveStateText.Text = "有未保存修改 · Ctrl+S 保存 · 3 分钟内自动保存";
        if (!_noteAutoSaveTimer.IsEnabled)
        {
            _noteAutoSaveTimer.Start();
        }
    }

    private void NoteBoldButton_Click(object sender, RoutedEventArgs e)
    {
        var currentWeight = NoteBodyEditor.Selection.GetPropertyValue(
            TextElement.FontWeightProperty);
        var isBold = currentWeight is FontWeight weight &&
                     weight == FontWeights.Bold;
        NoteBodyEditor.Selection.ApplyPropertyValue(
            TextElement.FontWeightProperty,
            isBold ? FontWeights.Normal : FontWeights.Bold);
        MarkCurrentNoteDirty();
        UpdateNoteBoldButtonState();
        NoteBodyEditor.Focus();
    }

    private void NoteTextColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string colorValue })
        {
            return;
        }

        Brush brush;
        if (string.Equals(colorValue, "Default", StringComparison.Ordinal))
        {
            brush = NoteBodyEditor.Foreground;
        }
        else
        {
            brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(colorValue));
        }

        NoteBodyEditor.Selection.ApplyPropertyValue(
            TextElement.ForegroundProperty,
            brush);
        MarkCurrentNoteDirty();
        NoteBodyEditor.Focus();
    }

    private void UpdateNoteBoldButtonState()
    {
        if (NoteBoldButton is null)
        {
            return;
        }

        var currentWeight = NoteBodyEditor.Selection.GetPropertyValue(
            TextElement.FontWeightProperty);
        var isBold = currentWeight is FontWeight weight &&
                     weight == FontWeights.Bold;
        NoteBoldButton.Background = isBold
            ? (Brush)FindResource("SelectionBrush")
            : Brushes.Transparent;
        NoteBoldButton.BorderBrush = isBold
            ? (Brush)FindResource("PrimaryBrush")
            : (Brush)FindResource("AppBorderBrush");
    }

    private async void NoteBodyTextBox_Pasting(
        object sender,
        DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Bitmap) &&
            e.DataObject.GetDataPresent(DataFormats.FileDrop) == false &&
            e.DataObject.GetDataPresent(DataFormats.UnicodeText) &&
            e.DataObject.GetData(DataFormats.UnicodeText) is string text)
        {
            e.DataObject = new DataObject(DataFormats.UnicodeText, text);
        }

        if (!TryGetClipboardImage(out var image))
        {
            return;
        }

        e.CancelCommand();
        await PasteImageAsync(image);
    }

    private async void NoteBodyTextBox_PreviewKeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.V &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            HasClipboardImage())
        {
            e.Handled = true;
            if (TryGetClipboardImage(out var image))
            {
                await PasteImageAsync(image);
            }
        }
    }

    private async void NoteBodyTextBox_CommandExecuted(
        object sender,
        ExecutedRoutedEventArgs e)
    {
        if (e.Command == EditingCommands.ToggleItalic ||
            e.Command == EditingCommands.ToggleUnderline)
        {
            e.Handled = true;
            return;
        }

        if (e.Command == EditingCommands.ToggleBold)
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    MarkCurrentNoteDirty();
                    UpdateNoteBoldButtonState();
                }));
            return;
        }

        if (e.Command == ApplicationCommands.Copy &&
            _selectedNoteImage is not null &&
            NoteBodyEditor.Selection.Text.Length <= 1)
        {
            CopyNoteImageToClipboard(_selectedNoteImage);
            e.Handled = true;
            return;
        }

        if (e.Command != ApplicationCommands.Paste)
        {
            return;
        }

        if (!HasClipboardImage())
        {
            _ = Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(NormalizeNoteBodyFontFamily));
            return;
        }

        e.Handled = true;
        if (TryGetClipboardImage(out var image))
        {
            await PasteImageAsync(image);
        }
    }

    private async Task PasteImageAsync(BitmapSource image)
    {
        if (_currentNote is null ||
            (_currentNote.IsEncrypted && _currentNoteKey is null))
        {
            StatusText.Text = "请先新建或解锁笔记，再粘贴图片。";
            return;
        }

        image = EnsureVisibleAlpha(image);
        if (image.CanFreeze)
        {
            image.Freeze();
        }

        try
        {
            var pngBytes = EncodeBitmap(image);
            string markdown;
            if (_currentNote.IsEncrypted)
            {
                var relativePath = await _notesService.SaveEncryptedAttachmentAsync(
                    _currentNote.Id,
                    ".png",
                    pngBytes,
                    _currentNoteKey!);
                markdown = $"![图片]({relativePath})";
            }
            else
            {
                var relativePath = await _notesService.SaveAttachmentAsync(
                    _currentNote.Id,
                    ".png",
                    pngBytes);
                markdown = $"![图片]({relativePath})";
            }

            InsertInlineImage(markdown, image);
            StatusText.Text = "图片已粘贴到笔记。";
        }
        catch (Exception exception)
        {
            ShowNoteError("粘贴图片失败", exception);
        }
    }

    private static bool HasClipboardImage()
    {
        try
        {
            return Clipboard.ContainsImage() ||
                   Clipboard.ContainsFileDropList() &&
                   Clipboard.GetFileDropList().Cast<string>().Any(IsImageFile);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetClipboardImage(out BitmapSource image)
    {
        image = null!;
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } bitmap)
            {
                image = bitmap;
                return true;
            }

            if (Clipboard.ContainsFileDropList())
            {
                var imagePath = Clipboard.GetFileDropList()
                    .Cast<string>()
                    .FirstOrDefault(IsImageFile);
                if (imagePath is not null)
                {
                    var fileImage = new BitmapImage();
                    fileImage.BeginInit();
                    fileImage.CacheOption = BitmapCacheOption.OnLoad;
                    fileImage.UriSource = new Uri(imagePath, UriKind.Absolute);
                    fileImage.EndInit();
                    fileImage.Freeze();
                    image = fileImage;
                    return true;
                }
            }
        }
        catch (Exception exception) when (
            exception is ExternalException or IOException or NotSupportedException)
        {
            AppDiagnostics.LogException("读取剪贴板图片", exception);
        }

        return false;
    }

    private static bool IsImageFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif";

    private void InsertInlineImage(string markdownReference, BitmapSource source)
    {
        if (!NoteBodyEditor.Selection.IsEmpty)
        {
            NoteBodyEditor.Selection.Text = "";
        }

        var caret = NoteBodyEditor.CaretPosition;
        var paragraph = caret.Paragraph;
        if (paragraph is null)
        {
            paragraph = new Paragraph();
            NoteBodyEditor.Document.Blocks.Add(paragraph);
            caret = paragraph.ContentEnd;
        }

        Inline? insertionTarget = null;
        var inlineList = paragraph.Inlines.ToList();
        for (var index = 0; index < inlineList.Count; index++)
        {
            var inline = inlineList[index];
            if (inline.ElementStart.CompareTo(caret) >= 0)
            {
                insertionTarget = inline;
                break;
            }

            if (inline is Run run &&
                inline.ElementStart.CompareTo(caret) < 0 &&
                inline.ElementEnd.CompareTo(caret) > 0)
            {
                var offset = new TextRange(run.ContentStart, caret).Text.Length;
                if (offset <= 0)
                {
                    insertionTarget = inline;
                }
                else if (offset >= run.Text.Length)
                {
                    insertionTarget = index + 1 < inlineList.Count
                        ? inlineList[index + 1]
                        : null;
                }
                else
                {
                    var trailingText = run.Text[offset..];
                    run.Text = run.Text[..offset];
                    var trailingRun = new Run(trailingText);
                    paragraph.Inlines.InsertAfter(run, trailingRun);
                    insertionTarget = trailingRun;
                }
                break;
            }
        }

        var container = CreateInlineImageContainer(markdownReference, source);
        if (insertionTarget is null)
        {
            paragraph.Inlines.Add(container);
        }
        else
        {
            paragraph.Inlines.InsertBefore(insertionTarget, container);
        }
        NoteBodyEditor.CaretPosition =
            container.ElementEnd.GetInsertionPosition(LogicalDirection.Forward)
            ?? container.ElementEnd;
        NoteBodyEditor.Focus();
        _noteDirty = true;
        _noteEditVersion++;
        NoteCharacterCountText.Text = $"{GetNoteBodyPlainText().Length:N0} 字符";
        NoteSaveStateText.Text = "有未保存修改 · 3 分钟内自动保存";
        if (!_noteAutoSaveTimer.IsEnabled)
        {
            _noteAutoSaveTimer.Start();
        }
    }

    private InlineUIContainer CreateInlineImageContainer(
        string markdownReference,
        BitmapSource source)
    {
        var previewGrid = new Grid();
        var imageFrame = new Border
        {
            Padding = new Thickness(5),
            Background = (Brush)FindResource("SurfaceAltBrush"),
            BorderBrush = (Brush)FindResource("AppBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = new Image
            {
                Source = source,
                MaxWidth = 620,
                MaxHeight = 360,
                Stretch = Stretch.Uniform
            }
        };
        previewGrid.Children.Add(imageFrame);
        previewGrid.Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 5, 5, 0),
            Padding = new Thickness(6, 2, 6, 2),
            Background = (Brush)FindResource("PrimaryBrush"),
            CornerRadius = new CornerRadius(5),
            Child = new TextBlock
            {
                Text = "复制",
                FontSize = 10,
                Foreground = (Brush)FindResource("OnPrimaryBrush")
            }
        });

        var copyTarget = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "单击复制原图，右键打开复制菜单",
            Child = previewGrid,
            Tag = imageFrame,
            Focusable = true
        };
        copyTarget.PreviewMouseLeftButtonDown += (_, e) =>
        {
            copyTarget.Focus();
            SelectInlineImage(imageFrame);
            CopyNoteImageToClipboard(source);
            e.Handled = true;
        };
        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = "复制图片" };
        copyItem.Click += (_, _) =>
        {
            SelectInlineImage(imageFrame);
            CopyNoteImageToClipboard(source);
        };
        menu.Items.Add(copyItem);
        copyTarget.ContextMenu = menu;
        return new InlineUIContainer(copyTarget)
        {
            BaselineAlignment = BaselineAlignment.Center,
            Tag = markdownReference
        };
    }

    private void SelectInlineImage(Border selectedFrame)
    {
        foreach (var paragraph in NoteBodyEditor.Document.Blocks.OfType<Paragraph>())
        {
            foreach (var container in paragraph.Inlines.OfType<InlineUIContainer>())
            {
                if (container.Child is Border { Tag: Border frame })
                {
                    frame.BorderBrush = ReferenceEquals(frame, selectedFrame)
                        ? (Brush)FindResource("PrimaryBrush")
                        : (Brush)FindResource("AppBorderBrush");
                    frame.BorderThickness = ReferenceEquals(frame, selectedFrame)
                        ? new Thickness(2)
                        : new Thickness(1);
                }
            }
        }
    }

    private string GetNoteBodyPlainText() =>
        new TextRange(
                NoteBodyEditor.Document.ContentStart,
                NoteBodyEditor.Document.ContentEnd)
            .Text;

    private string SerializeNoteBody()
    {
        var builder = new StringBuilder();
        var firstBlock = true;
        foreach (var block in NoteBodyEditor.Document.Blocks)
        {
            if (!firstBlock)
            {
                builder.Append('\n');
            }
            firstBlock = false;
            if (block is not Paragraph paragraph)
            {
                builder.Append(
                    new TextRange(block.ContentStart, block.ContentEnd).Text);
                continue;
            }

            foreach (var inline in paragraph.Inlines)
            {
                switch (inline)
                {
                    case Run run:
                        builder.Append(run.Text);
                        break;
                    case LineBreak:
                        builder.Append('\n');
                        break;
                    case InlineUIContainer { Tag: string reference }:
                        builder.Append(reference);
                        break;
                    default:
                        builder.Append(
                            new TextRange(inline.ContentStart, inline.ContentEnd).Text);
                        break;
                }
            }
        }

        return builder.ToString();
    }

    private string? SerializeNoteBodyRichText()
    {
        try
        {
            var data = new NoteRichTextData();
            var firstBlock = true;
            foreach (var block in NoteBodyEditor.Document.Blocks)
            {
                if (!firstBlock)
                {
                    data.Inlines.Add(new NoteRichInline { Kind = "line" });
                }
                firstBlock = false;

                if (block is not Paragraph paragraph)
                {
                    data.Inlines.Add(new NoteRichInline
                    {
                        Kind = "text",
                        Text = new TextRange(
                            block.ContentStart,
                            block.ContentEnd).Text
                    });
                    continue;
                }

                foreach (var inline in paragraph.Inlines)
                {
                    switch (inline)
                    {
                        case Run run:
                            data.Inlines.Add(new NoteRichInline
                            {
                                Kind = "text",
                                Text = run.Text,
                                FontFamily = TextElement.GetFontFamily(run).Source,
                                FontWeight = run.ReadLocalValue(
                                    TextElement.FontWeightProperty) is FontWeight
                                    ? TextElement.GetFontWeight(run).ToString()
                                    : null,
                                FontStyle = TextElement.GetFontStyle(run).ToString(),
                                FontStretch = TextElement.GetFontStretch(run).ToString(),
                                Foreground = (run.ReadLocalValue(
                                        TextElement.ForegroundProperty)
                                    as SolidColorBrush)?.Color.ToString()
                            });
                            break;
                        case LineBreak:
                            data.Inlines.Add(new NoteRichInline { Kind = "line" });
                            break;
                        case InlineUIContainer { Tag: string reference }:
                            data.Inlines.Add(new NoteRichInline
                            {
                                Kind = "image",
                                ImageReference = reference
                            });
                            break;
                        default:
                            data.Inlines.Add(new NoteRichInline
                            {
                                Kind = "text",
                                Text = new TextRange(
                                    inline.ContentStart,
                                    inline.ContentEnd).Text
                            });
                            break;
                    }
                }
            }

            return JsonSerializer.Serialize(data);
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("保存笔记富文本格式", exception);
            return null;
        }
    }

    private bool TryLoadNoteBodyRichText(string? richTextData)
    {
        if (string.IsNullOrWhiteSpace(richTextData))
        {
            return false;
        }

        try
        {
            var data = JsonSerializer.Deserialize<NoteRichTextData>(richTextData);
            if (data?.Version != 1 || data.Inlines.Count == 0)
            {
                return false;
            }

            var paragraph = new Paragraph { Margin = new Thickness(0) };
            foreach (var inline in data.Inlines)
            {
                if (inline.Kind == "line")
                {
                    paragraph.Inlines.Add(new LineBreak());
                    continue;
                }

                if (inline.Kind == "image" &&
                    !string.IsNullOrWhiteSpace(inline.ImageReference))
                {
                    var image = LoadNoteImage(
                        ExtractNoteImageUrl(inline.ImageReference));
                    if (image is not null)
                    {
                        paragraph.Inlines.Add(CreateInlineImageContainer(
                            inline.ImageReference,
                            image));
                    }
                    else
                    {
                        paragraph.Inlines.Add(new Run(inline.ImageReference));
                    }
                    continue;
                }

                var run = new Run(inline.Text);
                ApplyStoredTextFormatting(run, inline);
                paragraph.Inlines.Add(run);
            }

            NoteBodyEditor.Document.Blocks.Clear();
            NoteBodyEditor.Document.Blocks.Add(paragraph);
            NoteBodyEditor.CaretPosition = paragraph.ContentEnd;
            return true;
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("读取笔记富文本格式", exception);
            return false;
        }
    }

    private static string ExtractNoteImageUrl(string markdownReference)
    {
        var match = NoteImageRegex.Match(markdownReference);
        return match.Success
            ? match.Groups["url"].Value
            : markdownReference;
    }

    private void ApplyStoredTextFormatting(
        Run run,
        NoteRichInline inline)
    {
        if (!string.IsNullOrWhiteSpace(inline.FontFamily))
        {
            run.FontFamily = new FontFamily(inline.FontFamily);
        }
        if (!string.IsNullOrWhiteSpace(inline.FontWeight) &&
            new FontWeightConverter().ConvertFromInvariantString(
                inline.FontWeight) is FontWeight weight)
        {
            run.FontWeight = weight;
        }
        if (!string.IsNullOrWhiteSpace(inline.FontStyle) &&
            new FontStyleConverter().ConvertFromInvariantString(
                inline.FontStyle) is FontStyle style)
        {
            run.FontStyle = style;
        }
        if (!string.IsNullOrWhiteSpace(inline.FontStretch) &&
            new FontStretchConverter().ConvertFromInvariantString(
                inline.FontStretch) is FontStretch stretch)
        {
            run.FontStretch = stretch;
        }
        if (!string.IsNullOrWhiteSpace(inline.Foreground) &&
            ColorConverter.ConvertFromString(inline.Foreground) is Color color)
        {
            run.Foreground = new SolidColorBrush(color);
        }
    }

    private static byte[] EncodeBitmap(BitmapSource image)
    {
        image = EnsureVisibleAlpha(image);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static BitmapSource EnsureVisibleAlpha(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var hasVisibleAlpha = false;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                hasVisibleAlpha = true;
                break;
            }
        }

        if (hasVisibleAlpha)
        {
            if (converted.CanFreeze)
            {
                converted.Freeze();
            }
            return converted;
        }

        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        var repaired = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        repaired.Freeze();
        return repaired;
    }

    private void LoadNoteBodyMarkdown(string markdown)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var cursor = 0;
        foreach (Match match in NoteImageRegex.Matches(markdown))
        {
            if (match.Index > cursor)
            {
                AppendTextWithLineBreaks(
                    paragraph,
                    markdown[cursor..match.Index]);
            }

            var source = LoadNoteImage(match.Groups["url"].Value);
            if (source is null)
            {
                AppendTextWithLineBreaks(paragraph, match.Value);
            }
            else
            {
                paragraph.Inlines.Add(CreateInlineImageContainer(match.Value, source));
            }
            cursor = match.Index + match.Length;
        }

        if (cursor < markdown.Length)
        {
            AppendTextWithLineBreaks(paragraph, markdown[cursor..]);
        }

        NoteBodyEditor.Document.Blocks.Clear();
        NoteBodyEditor.Document.Blocks.Add(paragraph);
        NoteBodyEditor.CaretPosition = paragraph.ContentEnd;
    }

    private static void AppendTextWithLineBreaks(Paragraph paragraph, string text)
    {
        var lines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            if (lines[index].Length > 0)
            {
                paragraph.Inlines.Add(new Run(lines[index]));
            }
        }
    }

    private void CopyNoteImageToClipboard(BitmapSource image)
    {
        try
        {
            _selectedNoteImage = image;
            Clipboard.SetImage(image);
            StatusText.Text = "图片已复制到剪贴板。";
        }
        catch (Exception exception)
        {
            ShowNoteError("复制图片失败", exception);
        }
    }

    private void NoteBodyEditor_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            return;
        }

        e.Handled = true;
        var step = e.Delta > 0 ? 0.1 : -0.1;
        _noteZoom = Math.Clamp(
            Math.Round(_noteZoom + step, 2),
            0.5,
            2.0);
        ApplyNoteZoom();
    }

    private void NoteBodyEditor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (NoteBodyEditor.Selection.Text.Length > 1)
        {
            _selectedNoteImage = null;
        }
        UpdateNoteBoldButtonState();
    }

    private void ApplyNoteZoom()
    {
        NoteBodyEditor.FontSize = 14 * _noteZoom;
        NoteBodyEditor.Document.LineHeight = 22 * _noteZoom;
        NoteZoomText.Text = $"缩放 {_noteZoom:P0}";
        foreach (var paragraph in NoteBodyEditor.Document.Blocks.OfType<Paragraph>())
        {
            foreach (var container in paragraph.Inlines.OfType<InlineUIContainer>())
            {
                if (container.Child is Border { Child: Grid grid } &&
                    grid.Children.OfType<Border>().FirstOrDefault()?.Child is Image image)
                {
                    image.LayoutTransform = new ScaleTransform(_noteZoom, _noteZoom);
                }
            }
        }
    }

    private void NormalizeNoteBodyFontFamily()
    {
        if (NoteBodyEditor?.Document is null)
        {
            return;
        }

        var selectionStart = NoteBodyEditor.Selection.Start;
        var selectionEnd = NoteBodyEditor.Selection.End;
        var range = new TextRange(
            NoteBodyEditor.Document.ContentStart,
            NoteBodyEditor.Document.ContentEnd);
        range.ApplyPropertyValue(
            TextElement.FontFamilyProperty,
            NoteBodyEditor.FontFamily);
        range.ApplyPropertyValue(
            TextElement.FontStyleProperty,
            FontStyles.Normal);
        range.ApplyPropertyValue(
            TextElement.FontStretchProperty,
            FontStretches.Normal);
        NoteBodyEditor.Selection.Select(selectionStart, selectionEnd);
    }

    private BitmapSource? LoadNoteImage(string value)
    {
        try
        {
            if (value.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = value.IndexOf(',');
                if (comma < 0)
                {
                    return null;
                }

                var bytes = Convert.FromBase64String(value[(comma + 1)..]);
                using var stream = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                return EnsureVisibleAlpha(image);
            }

            if (_currentNote is null)
            {
                return null;
            }

            var attachmentBytes = _notesService.ReadAttachmentBytes(
                _currentNote.Id,
                value,
                _currentNoteKey);
            if (attachmentBytes is null)
            {
                return null;
            }

            using var attachmentStream = new MemoryStream(attachmentBytes);
            var fileImage = new BitmapImage();
            fileImage.BeginInit();
            fileImage.CacheOption = BitmapCacheOption.OnLoad;
            fileImage.StreamSource = attachmentStream;
            fileImage.EndInit();
            return EnsureVisibleAlpha(fileImage);
        }
        catch
        {
            return null;
        }
    }

    private async void NoteAutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        _noteAutoSaveTimer.Stop();
        try
        {
            await SaveCurrentNoteAsync(force: false);
        }
        catch (Exception exception)
        {
            NoteSaveStateText.Text = "自动保存失败";
            AppDiagnostics.LogException("自动保存笔记", exception);
        }
    }

    private async Task SaveCurrentNoteAsync(bool force)
    {
        if (_currentNote is null ||
            (_currentNote.IsEncrypted && _currentNoteKey is null))
        {
            return;
        }

        await _noteSaveGate.WaitAsync();
        try
        {
            var note = _currentNote;
            if (note is null ||
                (note.IsEncrypted && _currentNoteKey is null) ||
                (!force && !_noteDirty))
            {
                return;
            }

            var content = new NoteContent(
                NoteTitleTextBox.Text,
                SerializeNoteBody(),
                SerializeNoteBodyRichText());
            var editVersion = _noteEditVersion;
            if (note.IsEncrypted)
            {
                await _notesService.SaveEncryptedAsync(
                    note,
                    content,
                    _currentNoteKey!);
                if (editVersion == _noteEditVersion)
                {
                    SetNoteText(note.Title, note.Content, note.RichTextData);
                }
            }
            else
            {
                note.Title = content.Title;
                note.Content = content.Content;
                note.RichTextData = content.RichTextData;
                await _notesService.SaveAsync(note);
            }

            await RefreshNotesAsync(note.Id, activateSelected: false);
            if (editVersion == _noteEditVersion)
            {
                _noteDirty = false;
                NoteSaveStateText.Text = $"已保存 {DateTime.Now:HH:mm:ss}";
            }
            else
            {
                _noteDirty = true;
                NoteSaveStateText.Text = "保存期间有新修改，等待下次保存";
            }
        }
        finally
        {
            _noteSaveGate.Release();
        }
    }

    private bool ShouldSaveNoteBeforeClose() =>
        _currentNote is not null &&
        _noteDirty &&
        (!_currentNote.IsEncrypted || _currentNoteKey is not null);

    private void ContinueClosingAfterNoteDecision()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(Close));
    }

    private void LockCurrentNote(bool showCurrentState)
    {
        _noteAutoSaveTimer.Stop();
        _currentNoteKey?.Dispose();
        _currentNoteKey = null;
        _noteDirty = false;
        SetNoteText("", "");
        if (showCurrentState && _currentNote?.IsEncrypted == true)
        {
            _currentNote.Title = "";
            _currentNote.Content = "";
            ShowEncryptedNoteLocked();
            return;
        }

        _currentNote = null;
    }

    private void SetNoteText(
        string title,
        string content,
        string? richTextData = null)
    {
        _selectedNoteImage = null;
        _suppressNoteEditorEvents = true;
        try
        {
            NoteTitleTextBox.Text = title;
            if (!TryLoadNoteBodyRichText(richTextData))
            {
                LoadNoteBodyMarkdown(content);
            }
            NormalizeNoteBodyFontFamily();
            NoteCharacterCountText.Text = $"{content.Length:N0} 字符";
            ApplyNoteZoom();
            UpdateNoteBoldButtonState();
        }
        finally
        {
            _suppressNoteEditorEvents = false;
        }
    }

    private void ShowNoteError(string title, Exception exception)
    {
        var logPath = AppDiagnostics.LogException(title, exception);
        StatusText.Text = AppDiagnostics.GetUserMessage(exception);
        MessageBox.Show(
            this,
            $"{AppDiagnostics.GetUserMessage(exception)}\n\n诊断日志：\n{logPath}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
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
            var queryCorrection = _gitHub.LastQueryCorrection;

            if (_allRepositories.Count == 0)
            {
                CurrentGrid.ItemsSource = Array.Empty<RepositoryRecord>();
                ResetSearchResultSort(CurrentGrid);
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
            ResetSearchResultSort(CurrentGrid);
            GrowthGrid.ItemsSource = growth.Repositories;
            CurrentKeywordText.Text = queryCorrection is null
                ? keyword
                : $"{keyword} → {queryCorrection.CorrectedQuery}";
            ResultSummaryText.Text =
                $"领域匹配结果 · 匹配度优先 · 显示 {currentTop.Count:N0} 条 · " +
                $"本地周增长 {growth.Repositories.Count:N0} 条" +
                (queryCorrection is null ? "" : " · 已启用拼写纠错");
            LastGeneratedText.Text = $"生成于 {saved.GeneratedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
            StatusText.Text =
                (queryCorrection is null
                    ? "查询和报告保存完成。"
                    : $"未找到足够的“{keyword}”精确结果，已按“{queryCorrection.CorrectedQuery}”查询并保存报告。") +
                (_gitHub.HasToken ? "" : " 当前使用匿名 GitHub API；配置 Token 可提高额度。");
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

    private void SearchResultsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (sender is not DataGrid grid)
        {
            return;
        }

        var sortByStars = string.Equals(
            e.Column.SortMemberPath,
            nameof(RepositoryRecord.Stars),
            StringComparison.Ordinal);
        var sortByMatch = string.Equals(
                              e.Column.SortMemberPath,
                              nameof(RepositoryRecord.FullName),
                              StringComparison.Ordinal) ||
                          string.Equals(
                              e.Column.SortMemberPath,
                              nameof(RepositoryRecord.Rank),
                              StringComparison.Ordinal);
        if (!sortByStars && !sortByMatch)
        {
            return;
        }

        var direction = e.Column.SortDirection switch
        {
            null when sortByStars => ListSortDirection.Descending,
            null => ListSortDirection.Ascending,
            ListSortDirection.Ascending => ListSortDirection.Descending,
            _ => ListSortDirection.Ascending
        };

        e.Handled = true;
        if (grid.ItemsSource is null)
        {
            return;
        }

        var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
        if (!view.CanSort)
        {
            return;
        }

        using (view.DeferRefresh())
        {
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(
                new SortDescription(
                    sortByStars ? nameof(RepositoryRecord.Stars) : nameof(RepositoryRecord.Rank),
                    direction));
            if (sortByStars)
            {
                view.SortDescriptions.Add(
                    new SortDescription(nameof(RepositoryRecord.Rank), ListSortDirection.Ascending));
            }
        }

        foreach (var column in grid.Columns)
        {
            column.SortDirection = null;
        }
        e.Column.SortDirection = direction;
    }

    private static void ResetSearchResultSort(DataGrid grid)
    {
        if (grid.ItemsSource is not null)
        {
            var view = CollectionViewSource.GetDefaultView(grid.ItemsSource);
            view.SortDescriptions.Clear();
        }

        foreach (var column in grid.Columns)
        {
            column.SortDirection = null;
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
        StatusText.Text = "正在保存笔记并导出所有记录、收藏和报告...";
        try
        {
            await SaveCurrentNoteAsync(force: true);
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
            "导入会替换当前电脑上的笔记、报告、查询记录、收藏和 Star 快照。\n\n" +
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
            LockCurrentNote(showCurrentState: false);
            _noteItems = [];
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
        SettingsButton.IsEnabled = enabled;
    }

    private static string GetAppVersion() =>
        typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    private static string FormatBytes(long bytes) =>
        bytes >= 1024L * 1024L * 1024L
            ? $"{bytes / 1024d / 1024d / 1024d:F2} GB"
            : bytes >= 1024L * 1024L
                ? $"{bytes / 1024d / 1024d:F2} MB"
                : $"{bytes / 1024d:F1} KB";

    private void RestartApplication()
    {
        _isExiting = true;
        DisposeTrayIcon();
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

        if (!ReferenceEquals(WorkspaceTabs.SelectedItem, NotesTab) &&
            _currentNote is not null)
        {
            try
            {
                await SaveCurrentNoteAsync(force: true);
            }
            catch (Exception exception)
            {
                AppDiagnostics.LogException("离开笔记页前保存", exception);
            }
            finally
            {
                LockCurrentNote(showCurrentState: false);
            }
        }
        else if (ReferenceEquals(WorkspaceTabs.SelectedItem, NotesTab) &&
                 NoteTree.SelectedItem is NoteTreeItem { Note: { } selectedNote })
        {
            await ActivateNoteAsync(selectedNote);
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
            280);
        SetDefaultPanelWidths(
            FindLeftColumn,
            FindCenterColumn,
            FindRightColumn,
            280);
        SetDefaultPanelWidths(
            GlobalLeftColumn,
            GlobalCenterColumn,
            GlobalRightColumn,
            280);
        NotesLeftColumn.Width = new GridLength(280);
        SaveLayoutSettings();
        StatusText.Text = "已恢复默认面板布局。";
    }

    private void PanelSplitter_DragCompleted(
        object sender,
        DragCompletedEventArgs e) =>
        SaveLayoutSettings();

    private void ApplySavedLayout()
    {
        Width = Math.Max(
            MinWidth,
            Math.Min(_appSettings.WindowWidth, SystemParameters.WorkArea.Width));
        Height = Math.Max(
            MinHeight,
            Math.Min(_appSettings.WindowHeight, SystemParameters.WorkArea.Height));
        ApplyPanelWidths(
            CombinedLeftColumn,
            CombinedCenterColumn,
            CombinedRightColumn,
            _appSettings.CombinedLeftPanelWidth,
            _appSettings.CombinedRightPanelWidth);
        ApplyPanelWidths(
            FindLeftColumn,
            FindCenterColumn,
            FindRightColumn,
            _appSettings.FindLeftPanelWidth,
            _appSettings.FindRightPanelWidth);
        ApplyPanelWidths(
            GlobalLeftColumn,
            GlobalCenterColumn,
            GlobalRightColumn,
            _appSettings.GlobalLeftPanelWidth,
            _appSettings.GlobalRightPanelWidth);
        NotesLeftColumn.Width = new GridLength(
            Math.Max(NotesLeftColumn.MinWidth, _appSettings.NotesLeftPanelWidth));
    }

    private void SaveLayoutSettings()
    {
        var bounds = WindowState == System.Windows.WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (bounds.Width >= MinWidth && bounds.Height >= MinHeight)
        {
            _appSettings.WindowWidth = bounds.Width;
            _appSettings.WindowHeight = bounds.Height;
        }

        if (WindowState != System.Windows.WindowState.Minimized)
        {
            _appSettings.WindowMaximized =
                WindowState == System.Windows.WindowState.Maximized;
        }
        _appSettings.CombinedLeftPanelWidth = GetPanelWidth(CombinedLeftColumn);
        _appSettings.CombinedRightPanelWidth = GetPanelWidth(CombinedRightColumn);
        _appSettings.FindLeftPanelWidth = GetPanelWidth(FindLeftColumn);
        _appSettings.FindRightPanelWidth = GetPanelWidth(FindRightColumn);
        _appSettings.GlobalLeftPanelWidth = GetPanelWidth(GlobalLeftColumn);
        _appSettings.GlobalRightPanelWidth = GetPanelWidth(GlobalRightColumn);
        _appSettings.NotesLeftPanelWidth = GetPanelWidth(NotesLeftColumn);

        try
        {
            _appSettingsService.Save(_appSettings);
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("保存窗口布局", exception);
        }
    }

    private static double GetPanelWidth(ColumnDefinition column) =>
        column.Width.IsAbsolute
            ? column.Width.Value
            : column.ActualWidth;

    private static void ApplyPanelWidths(
        ColumnDefinition left,
        ColumnDefinition center,
        ColumnDefinition right,
        double leftWidth,
        double rightWidth)
    {
        left.Width = new GridLength(Math.Max(left.MinWidth, leftWidth));
        center.Width = new GridLength(1, GridUnitType.Star);
        right.Width = new GridLength(Math.Max(right.MinWidth, rightWidth));
    }

    private static void SetDefaultPanelWidths(
        ColumnDefinition left,
        ColumnDefinition center,
        ColumnDefinition right,
        double rightWidth)
    {
        left.Width = new GridLength(220);
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
            var queryCorrection = _gitHub.LastQueryCorrection;
            StandaloneSearchResultsGrid.ItemsSource = _standaloneSearchResults;
            ResetSearchResultSort(StandaloneSearchResultsGrid);
            CombinedContentTabs.SelectedItem = CombinedSearchResultsTab;
            StandaloneSearchTitleText.Text = queryCorrection is null
                ? keyword
                : $"{keyword} → {queryCorrection.CorrectedQuery}";
            StandaloneSearchSummaryText.Text =
                $"GitHub 领域相关结果 · 匹配度优先 · " +
                $"{_standaloneSearchResults.Count:N0} 条 · " +
                "选择底部目录后可保存整份结果" +
                (queryCorrection is null ? "" : " · 已启用拼写纠错");
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
                : queryCorrection is null
                    ? $"搜索完成，共 {_standaloneSearchResults.Count:N0} 条。"
                    : $"未找到足够的“{keyword}”精确结果，已按“{queryCorrection.CorrectedQuery}”搜索，" +
                      $"共 {_standaloneSearchResults.Count:N0} 条。";
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
            CombinedFavoritesGrid.ItemsSource = selectedFolder?.Repositories
                .OrderBy(repository => repository.SortOrder)
                .ThenByDescending(repository => repository.SavedAt)
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
        CombinedFavoritesGrid.ItemsSource = _selectedSearchFolder?.Repositories
            .OrderBy(repository => repository.SortOrder)
            .ThenByDescending(repository => repository.SavedAt)
            .ToList() ?? [];
        _selectedCombinedFavoriteRepository = null;
        if (ReferenceEquals(CombinedContentTabs.SelectedItem, CombinedDirectoryTab))
        {
            ClearStandaloneDetails("在目录内容中选择一个仓库查看详情。");
        }
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

    private async void RenameSearchDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FavoriteFolder folder })
        {
            return;
        }

        SearchDirectoryList.SelectedItem = folder;
        var dialog = new FolderNameDialog(
            "重命名收藏目录",
            "输入新的目录名称。",
            folder.Name)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var folderId = folder.Id;
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

    private async void DeleteSearchDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FavoriteFolder folder })
        {
            return;
        }

        var message = folder.Repositories.Count == 0
            ? $"确定删除收藏目录“{folder.Name}”吗？"
            : $"确定删除收藏目录“{folder.Name}”吗？\n\n" +
              $"目录中的 {folder.Repositories.Count:N0} 个收藏项目及备注也会从本地删除。";
        var result = MessageBox.Show(
            this,
            message + "\n\n此操作不可撤销。",
            "删除收藏目录",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            SearchDirectoryList.SelectedItem = folder;
            var removed = await _favorites.DeleteFolderAsync(folder.Id);
            if (!removed)
            {
                throw new InvalidOperationException("所选收藏目录已经不存在。");
            }

            await RefreshSearchDirectoriesAsync();
            StatusText.Text = $"已删除收藏目录“{folder.Name}”。";
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
            if (ReferenceEquals(CombinedContentTabs.SelectedItem, CombinedSearchResultsTab))
            {
                ClearStandaloneDetails("在搜索结果中选择一个仓库查看详情。");
            }
            UpdateStandaloneSearchActionState();
            return;
        }

        if (ReferenceEquals(CombinedContentTabs.SelectedItem, CombinedSearchResultsTab))
        {
            ShowSearchRepositoryDetails(_selectedStandaloneRepository);
        }

        UpdateStandaloneSearchActionState();
    }

    private void ShowSearchRepositoryDetails(RepositoryRecord repository)
    {
        FavoriteNoteButton.Visibility = Visibility.Collapsed;
        StandaloneNoteSection.Visibility = Visibility.Collapsed;
        StandaloneDetailNoteText.Text = "尚未添加备注。";
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

    private void FavoriteFolderList_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ResetFavoriteFolderDrag();
        if (sender is not ListBox list ||
            e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject originalSource)
        {
            return;
        }

        var hitSource =
            list.InputHitTest(e.GetPosition(list))
                as DependencyObject
            ?? originalSource;
        if (FindVisualAncestor<Button>(hitSource) is not null ||
            FindVisualAncestor<ScrollBar>(hitSource) is not null)
        {
            return;
        }

        var item = FindVisualAncestor<ListBoxItem>(hitSource);
        if (item?.DataContext is not FavoriteFolder folder)
        {
            return;
        }

        list.SelectedItem = folder;
        _favoriteFolderDragList = list;
        _favoriteFolderDragFolder = folder;
        _favoriteFolderDragOriginalItems = list.Items
            .Cast<FavoriteFolder>()
            .ToList();
        _favoriteFolderDragOriginalSortOrders = _favoriteFolderDragOriginalItems
            .ToDictionary(item => item.Id, item => item.SortOrder);
        _favoriteFolderDragOrderChanged = false;
        _favoriteFolderDragTimer.Start();
    }

    private async void FavoriteFolderList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_favoriteFolderDragFolder is null ||
            _favoriteFolderDragList is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_favoriteFolderDragArmed)
            {
                await FinishFavoriteFolderDragAsync();
            }
            else
            {
                ResetFavoriteFolderDrag();
            }
            return;
        }

        if (!_favoriteFolderDragArmed)
        {
            return;
        }

        var currentPoint = e.GetPosition(_favoriteFolderDragList);
        ShowFavoriteFolderDragGhost(currentPoint);
        ReorderFavoriteFolderAtPoint(currentPoint);
    }

    private async void FavoriteFolderList_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        await FinishFavoriteFolderDragAsync();

    private void FavoriteFolderDragTimer_Tick(object? sender, EventArgs e)
    {
        if (_favoriteFolderDragFolder is null ||
            _favoriteFolderDragList is null ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            ResetFavoriteFolderDrag();
            return;
        }

        _favoriteFolderDragArmed = true;
        _favoriteFolderDragList.CaptureMouse();
        _favoriteFolderDragList.Cursor = Cursors.SizeAll;
        ShowFavoriteFolderDragGhost(Mouse.GetPosition(_favoriteFolderDragList));
    }

    private void ReorderFavoriteFolderAtPoint(Point pointerPosition)
    {
        if (_favoriteFolderDragFolder is null ||
            _favoriteFolderDragList is null ||
            _favoriteFolderDragList.InputHitTest(pointerPosition) is not DependencyObject hitTest)
        {
            return;
        }

        var targetItem = FindVisualAncestor<ListBoxItem>(hitTest);
        if (targetItem?.DataContext is FavoriteFolder)
        {
            ReorderFavoriteFolderRelativeTo(targetItem, pointerPosition.Y);
        }
    }

    private void ReorderFavoriteFolderRelativeTo(
        ListBoxItem targetItem,
        double pointerY)
    {
        if (_favoriteFolderDragFolder is null ||
            _favoriteFolderDragList is null ||
            targetItem.DataContext is not FavoriteFolder targetFolder)
        {
            return;
        }

        var items = _favoriteFolderDragList.Items.Cast<FavoriteFolder>().ToList();
        var sourceIndex = items.FindIndex(folder => folder.Id == _favoriteFolderDragFolder.Id);
        var targetIndex = items.FindIndex(folder => folder.Id == targetFolder.Id);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var dropBelow =
            pointerY -
            targetItem.TranslatePoint(new Point(0, 0), _favoriteFolderDragList).Y >
            targetItem.ActualHeight / 2;
        var insertIndex = targetIndex + (dropBelow ? 1 : 0);
        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        var itemPositions = CaptureFavoriteFolderItemPositions(_favoriteFolderDragList);
        var movedFolder = items[sourceIndex];
        items.RemoveAt(sourceIndex);
        items.Insert(insertIndex, movedFolder);
        for (var index = 0; index < items.Count; index++)
        {
            items[index].SortOrder = index;
        }

        ApplyFavoriteFolderOrder(items);
        AnimateFavoriteFolderItems(_favoriteFolderDragList, itemPositions);
        _favoriteFolderDragList.SelectedItem = movedFolder;
        _favoriteFolderDragList.ScrollIntoView(movedFolder);
        _favoriteFolderDragOrderChanged = true;
    }

    private async Task FinishFavoriteFolderDragAsync()
    {
        var activeList = _favoriteFolderDragList;
        var movedFolder = _favoriteFolderDragFolder;
        var originalItems = _favoriteFolderDragOriginalItems;
        var originalSortOrders = _favoriteFolderDragOriginalSortOrders;
        var orderChanged = _favoriteFolderDragOrderChanged;
        var items = activeList?.Items.Cast<FavoriteFolder>().ToList() ?? [];
        ResetFavoriteFolderDrag();
        if (!orderChanged ||
            movedFolder is null ||
            originalItems is null ||
            originalSortOrders is null)
        {
            return;
        }

        try
        {
            await _favorites.ReorderFoldersAsync(
                items.Select(folder => folder.Id).ToList());
            StatusText.Text = $"已调整收藏目录“{movedFolder.Name}”的顺序。";
        }
        catch (Exception exception)
        {
            var itemPositions = activeList is null
                ? new Dictionary<string, double>()
                : CaptureFavoriteFolderItemPositions(activeList);
            foreach (var folder in originalItems)
            {
                folder.SortOrder = originalSortOrders[folder.Id];
            }
            ApplyFavoriteFolderOrder(originalItems);
            if (activeList is not null)
            {
                AnimateFavoriteFolderItems(activeList, itemPositions);
            }
            ShowFavoriteError(exception);
            return;
        }

        try
        {
            await RefreshSearchDirectoriesAsync(movedFolder.Id);
        }
        catch (Exception exception)
        {
            AppDiagnostics.LogException("刷新收藏目录顺序", exception);
        }
    }

    private void ApplyFavoriteFolderOrder(IReadOnlyList<FavoriteFolder> orderedFolders)
    {
        var orderedIds = orderedFolders.Select(folder => folder.Id).ToList();
        var favoriteSelectionId =
            (FavoriteFolderList.SelectedItem as FavoriteFolder)?.Id;
        var searchSelectionId =
            (SearchDirectoryList.SelectedItem as FavoriteFolder)?.Id;
        var destinationId =
            (SearchFavoriteFolderComboBox.SelectedItem as FavoriteFolder)?.Id;

        _favoriteFolders = ReorderFolderCollection(_favoriteFolders, orderedFolders, orderedIds);
        _searchFolders = ReorderFolderCollection(_searchFolders, orderedFolders, orderedIds);
        FavoriteFolderList.ItemsSource = _favoriteFolders;
        SearchDirectoryList.ItemsSource = _searchFolders;
        SearchFavoriteFolderComboBox.ItemsSource = _favoriteFolders;
        FavoriteFolderList.SelectedItem =
            _favoriteFolders.FirstOrDefault(folder => folder.Id == favoriteSelectionId)
            ?? _favoriteFolders.FirstOrDefault();
        SearchDirectoryList.SelectedItem =
            _searchFolders.FirstOrDefault(folder => folder.Id == searchSelectionId)
            ?? _searchFolders.FirstOrDefault();
        SearchFavoriteFolderComboBox.SelectedItem =
            _favoriteFolders.FirstOrDefault(folder => folder.Id == destinationId)
            ?? _favoriteFolders.FirstOrDefault();
    }

    private static List<FavoriteFolder> ReorderFolderCollection(
        IReadOnlyList<FavoriteFolder> current,
        IReadOnlyList<FavoriteFolder> orderedFallback,
        IReadOnlyList<string> orderedIds)
    {
        var byId = current.ToDictionary(folder => folder.Id);
        return current.Count == orderedIds.Count &&
               orderedIds.All(byId.ContainsKey)
            ? orderedIds.Select(id => byId[id]).ToList()
            : orderedFallback.ToList();
    }

    private static Dictionary<string, double> CaptureFavoriteFolderItemPositions(ListBox list)
    {
        var positions = new Dictionary<string, double>();
        foreach (var folder in list.Items.Cast<FavoriteFolder>())
        {
            if (list.ItemContainerGenerator.ContainerFromItem(folder)
                is not ListBoxItem item)
            {
                continue;
            }

            positions[folder.Id] = item.TranslatePoint(
                new Point(0, 0),
                list).Y;
        }

        return positions;
    }

    private void AnimateFavoriteFolderItems(
        ListBox list,
        IReadOnlyDictionary<string, double> previousPositions)
    {
        var animationVersion = ++_favoriteFolderOrderAnimationVersion;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                list.UpdateLayout();
                foreach (var folder in list.Items.Cast<FavoriteFolder>())
                {
                    if (!previousPositions.TryGetValue(folder.Id, out var previousY) ||
                        list.ItemContainerGenerator.ContainerFromItem(folder)
                        is not ListBoxItem item)
                    {
                        continue;
                    }

                    var currentY = item.TranslatePoint(
                        new Point(0, 0),
                        list).Y;
                    var offset = previousY - currentY;
                    if (Math.Abs(offset) < 0.5)
                    {
                        continue;
                    }

                    var transform = new TranslateTransform(0, offset);
                    item.RenderTransform = transform;
                    var animation = new DoubleAnimation(
                        offset,
                        0,
                        FavoriteOrderAnimationDuration)
                    {
                        EasingFunction = new CubicEase
                        {
                            EasingMode = EasingMode.EaseOut
                        },
                        FillBehavior = FillBehavior.HoldEnd
                    };
                    animation.Completed += (_, _) =>
                    {
                        if (animationVersion != _favoriteFolderOrderAnimationVersion)
                        {
                            return;
                        }

                        transform.Y = 0;
                        item.ClearValue(RenderTransformProperty);
                    };
                    transform.BeginAnimation(TranslateTransform.YProperty, animation);
                }
            }));
    }

    private void ShowFavoriteFolderDragGhost(Point position)
    {
        if (_favoriteFolderDragFolder is null ||
            _favoriteFolderDragList is null)
        {
            return;
        }

        if (_favoriteFolderDragGhost is null)
        {
            _favoriteFolderDragAdornerLayer =
                AdornerLayer.GetAdornerLayer(_favoriteFolderDragList);
            if (_favoriteFolderDragAdornerLayer is null)
            {
                return;
            }

            _favoriteFolderDragGhost = new FavoriteDragGhostAdorner(
                _favoriteFolderDragList,
                _favoriteFolderDragFolder.Name,
                "拖动以调整目录顺序",
                Math.Clamp(_favoriteFolderDragList.ActualWidth - 16, 180, 300));
            _favoriteFolderDragAdornerLayer.Add(_favoriteFolderDragGhost);
        }

        _favoriteFolderDragGhost.UpdatePosition(position);
    }

    private void ResetFavoriteFolderDrag()
    {
        _favoriteFolderDragTimer.Stop();
        if (_favoriteFolderDragList?.IsMouseCaptured == true)
        {
            _favoriteFolderDragList.ReleaseMouseCapture();
        }
        _favoriteFolderDragArmed = false;
        _favoriteFolderDragOrderChanged = false;
        if (_favoriteFolderDragList is not null)
        {
            _favoriteFolderDragList.Cursor = Cursors.Arrow;
        }
        _favoriteFolderDragList = null;
        _favoriteFolderDragFolder = null;
        _favoriteFolderDragOriginalItems = null;
        _favoriteFolderDragOriginalSortOrders = null;
        if (_favoriteFolderDragGhost is not null &&
            _favoriteFolderDragAdornerLayer is not null)
        {
            _favoriteFolderDragAdornerLayer.Remove(_favoriteFolderDragGhost);
        }
        _favoriteFolderDragGhost = null;
        _favoriteFolderDragAdornerLayer = null;
    }

    private void CombinedFavoritesGrid_PreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        ResetFavoriteDrag();
        ResetFavoriteFolderDrag();
        if (e.ChangedButton != MouseButton.Left ||
            e.OriginalSource is not DependencyObject originalSource ||
            FindVisualAncestor<DataGridColumnHeader>(originalSource) is not null)
        {
            return;
        }

        var hitSource =
            CombinedFavoritesGrid.InputHitTest(e.GetPosition(CombinedFavoritesGrid))
                as DependencyObject
            ?? originalSource;
        var nameText = FindVisualAncestor<TextBlock>(hitSource);
        var row = FindVisualAncestor<DataGridRow>(hitSource);
        if (nameText?.Tag as string != "FavoriteRepositoryName" ||
            row?.Item is not FavoriteRepository repository)
        {
            return;
        }

        CombinedFavoritesGrid.SelectedItem = repository;
        _favoriteDragRepository = repository;
        _favoriteDragOriginalItems = CombinedFavoritesGrid.Items
            .Cast<FavoriteRepository>()
            .ToList();
        _favoriteDragOriginalSortOrders = _favoriteDragOriginalItems.ToDictionary(
            item => item.Id,
            item => item.SortOrder);
        _favoriteDragOrderChanged = false;
        _favoriteDragTimer.Start();
    }

    private async void CombinedFavoritesGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_favoriteDragRepository is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_favoriteDragArmed)
            {
                await FinishFavoriteDragAsync();
            }
            else
            {
                ResetFavoriteDrag();
            }
            return;
        }

        var currentPoint = e.GetPosition(CombinedFavoritesGrid);
        if (!_favoriteDragArmed)
        {
            return;
        }

        ShowFavoriteDragGhost(currentPoint);
        ReorderFavoriteAtPoint(currentPoint);
    }

    private async void CombinedFavoritesGrid_PreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e) =>
        await FinishFavoriteDragAsync();

    private void FavoriteDragTimer_Tick(object? sender, EventArgs e)
    {
        if (_favoriteDragRepository is null ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            ResetFavoriteDrag();
            return;
        }

        _favoriteDragArmed = true;
        CombinedFavoritesGrid.CaptureMouse();
        CombinedFavoritesGrid.Cursor = Cursors.SizeAll;
        ShowFavoriteDragGhost(Mouse.GetPosition(CombinedFavoritesGrid));
    }

    private void ReorderFavoriteAtPoint(Point pointerPosition)
    {
        if (_favoriteDragRepository is null ||
            _selectedSearchFolder is null ||
            CombinedFavoritesGrid.InputHitTest(pointerPosition) is not DependencyObject hitTest)
        {
            return;
        }

        var targetRow = FindVisualAncestor<DataGridRow>(hitTest);
        if (targetRow?.Item is not FavoriteRepository targetRepository)
        {
            return;
        }

        ReorderFavoriteRelativeTo(targetRow, pointerPosition.Y);
    }

    private void ReorderFavoriteRelativeTo(
        DataGridRow targetRow,
        double pointerY)
    {
        if (_favoriteDragRepository is null ||
            _selectedSearchFolder is null ||
            targetRow.Item is not FavoriteRepository targetRepository)
        {
            return;
        }

        var items = CombinedFavoritesGrid.Items
            .Cast<FavoriteRepository>()
            .ToList();
        var sourceIndex = items.FindIndex(
            repository => repository.Id == _favoriteDragRepository.Id);
        var targetIndex = items.FindIndex(
            repository => repository.Id == targetRepository.Id);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        var dropBelow =
            pointerY -
            targetRow.TranslatePoint(new Point(0, 0), CombinedFavoritesGrid).Y >
            targetRow.ActualHeight / 2;
        var insertIndex = targetIndex + (dropBelow ? 1 : 0);

        if (sourceIndex < insertIndex)
        {
            insertIndex--;
        }

        if (insertIndex == sourceIndex)
        {
            return;
        }

        var rowPositions = CaptureFavoriteRowPositions();
        var movedRepository = items[sourceIndex];
        items.RemoveAt(sourceIndex);
        items.Insert(insertIndex, movedRepository);
        for (var index = 0; index < items.Count; index++)
        {
            items[index].SortOrder = index;
        }

        ApplyFavoriteOrder(_selectedSearchFolder, items);
        AnimateFavoriteRows(rowPositions);
        CombinedFavoritesGrid.SelectedItem = movedRepository;
        CombinedFavoritesGrid.ScrollIntoView(movedRepository);
        _favoriteDragOrderChanged = true;
    }

    private async Task FinishFavoriteDragAsync()
    {
        var folder = _selectedSearchFolder;
        var movedRepository = _favoriteDragRepository;
        var originalItems = _favoriteDragOriginalItems;
        var originalSortOrders = _favoriteDragOriginalSortOrders;
        var orderChanged = _favoriteDragOrderChanged;
        var items = CombinedFavoritesGrid.Items
            .Cast<FavoriteRepository>()
            .ToList();
        ResetFavoriteDrag();
        if (!orderChanged ||
            folder is null ||
            movedRepository is null ||
            originalItems is null ||
            originalSortOrders is null)
        {
            return;
        }

        try
        {
            await _favorites.ReorderFavoritesAsync(
                folder.Id,
                items.Select(repository => repository.Id).ToList());
            StatusText.Text = $"已调整“{movedRepository.FullName}”的收藏顺序。";
        }
        catch (Exception exception)
        {
            var rowPositions = CaptureFavoriteRowPositions();
            foreach (var repository in originalItems)
            {
                repository.SortOrder = originalSortOrders[repository.Id];
            }
            ApplyFavoriteOrder(folder, originalItems);
            AnimateFavoriteRows(rowPositions);
            ShowFavoriteError(exception);
        }
    }

    private void ApplyFavoriteOrder(
        FavoriteFolder folder,
        IReadOnlyList<FavoriteRepository> orderedRepositories)
    {
        folder.Repositories.Clear();
        folder.Repositories.AddRange(orderedRepositories);
        CombinedFavoritesGrid.ItemsSource = orderedRepositories.ToList();
    }

    private Dictionary<long, double> CaptureFavoriteRowPositions()
    {
        var positions = new Dictionary<long, double>();
        foreach (var repository in CombinedFavoritesGrid.Items.Cast<FavoriteRepository>())
        {
            if (CombinedFavoritesGrid.ItemContainerGenerator.ContainerFromItem(repository)
                is not DataGridRow row)
            {
                continue;
            }

            positions[repository.Id] = row.TranslatePoint(
                new Point(0, 0),
                CombinedFavoritesGrid).Y;
        }

        return positions;
    }

    private void AnimateFavoriteRows(IReadOnlyDictionary<long, double> previousPositions)
    {
        var animationVersion = ++_favoriteOrderAnimationVersion;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                CombinedFavoritesGrid.UpdateLayout();
                foreach (var repository in CombinedFavoritesGrid.Items.Cast<FavoriteRepository>())
                {
                    if (!previousPositions.TryGetValue(repository.Id, out var previousY) ||
                        CombinedFavoritesGrid.ItemContainerGenerator.ContainerFromItem(repository)
                        is not DataGridRow row)
                    {
                        continue;
                    }

                    var currentY = row.TranslatePoint(
                        new Point(0, 0),
                        CombinedFavoritesGrid).Y;
                    var offset = previousY - currentY;
                    if (Math.Abs(offset) < 0.5)
                    {
                        continue;
                    }

                    var transform = new TranslateTransform(0, offset);
                    row.RenderTransform = transform;
                    var animation = new DoubleAnimation(
                        offset,
                        0,
                        FavoriteOrderAnimationDuration)
                    {
                        EasingFunction = new CubicEase
                        {
                            EasingMode = EasingMode.EaseOut
                        },
                        FillBehavior = FillBehavior.HoldEnd
                    };
                    animation.Completed += (_, _) =>
                    {
                        if (animationVersion != _favoriteOrderAnimationVersion)
                        {
                            return;
                        }

                        transform.Y = 0;
                        row.ClearValue(RenderTransformProperty);
                    };
                    transform.BeginAnimation(TranslateTransform.YProperty, animation);
                }
            }));
    }

    private void ShowFavoriteDragGhost(Point position)
    {
        if (_favoriteDragRepository is null)
        {
            return;
        }

        if (_favoriteDragGhost is null)
        {
            _favoriteDragAdornerLayer = AdornerLayer.GetAdornerLayer(CombinedFavoritesGrid);
            if (_favoriteDragAdornerLayer is null)
            {
                return;
            }

            _favoriteDragGhost = new FavoriteDragGhostAdorner(
                CombinedFavoritesGrid,
                _favoriteDragRepository.FullName);
            _favoriteDragAdornerLayer.Add(_favoriteDragGhost);
        }

        _favoriteDragGhost.UpdatePosition(position);
    }

    private void ResetFavoriteDrag()
    {
        _favoriteDragTimer.Stop();
        if (CombinedFavoritesGrid.IsMouseCaptured)
        {
            CombinedFavoritesGrid.ReleaseMouseCapture();
        }
        _favoriteDragArmed = false;
        _favoriteDragOrderChanged = false;
        _favoriteDragRepository = null;
        _favoriteDragOriginalItems = null;
        _favoriteDragOriginalSortOrders = null;
        CombinedFavoritesGrid.Cursor = Cursors.Arrow;
        if (_favoriteDragGhost is not null &&
            _favoriteDragAdornerLayer is not null)
        {
            _favoriteDragAdornerLayer.Remove(_favoriteDragGhost);
        }
        _favoriteDragGhost = null;
        _favoriteDragAdornerLayer = null;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null && current is not T)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        return current as T;
    }

    private async void CombinedFavoriteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FavoriteRepository repository } ||
            _selectedSearchFolder is not { } folder)
        {
            return;
        }

        CombinedFavoritesGrid.SelectedItem = repository;
        await EditFavoriteNoteAsync(folder, repository);
    }

    private void CombinedFavoritesGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        _selectedCombinedFavoriteRepository =
            CombinedFavoritesGrid.SelectedItem as FavoriteRepository;
        if (ReferenceEquals(CombinedContentTabs.SelectedItem, CombinedDirectoryTab))
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

        if (ReferenceEquals(CombinedContentTabs.SelectedItem, CombinedDirectoryTab))
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
        UpdateFavoriteNotePresentation(repository);
    }

    private void UpdateFavoriteNotePresentation(FavoriteRepository repository)
    {
        FavoriteNoteButton.Visibility = Visibility.Visible;
        FavoriteNoteButton.Content = repository.HasNote ? "查看备注" : "添加备注";
        StandaloneNoteSection.Visibility = Visibility.Visible;
        StandaloneDetailNoteText.Text = repository.HasNote
            ? repository.Note
            : "尚未添加备注。";
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
        FavoriteNoteButton.Visibility = Visibility.Collapsed;
        StandaloneNoteSection.Visibility = Visibility.Collapsed;
        StandaloneDetailNoteText.Text = "尚未添加备注。";
    }

    private async void FavoriteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSearchFolder is not { } folder ||
            _selectedCombinedFavoriteRepository is not { } repository)
        {
            return;
        }

        await EditFavoriteNoteAsync(folder, repository);
    }

    private async Task EditFavoriteNoteAsync(
        FavoriteFolder folder,
        FavoriteRepository repository)
    {
        var dialog = new FavoriteNoteDialog(repository.FullName, repository.Note)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _favorites.SetNoteAsync(folder.Id, repository.Id, dialog.Note);
            repository.Note = dialog.Note;
            FavoriteNoteButton.Content = repository.HasNote ? "查看备注" : "添加备注";
            StandaloneDetailNoteText.Text = repository.HasNote
                ? repository.Note
                : "尚未添加备注。";
            CombinedFavoritesGrid.Items.Refresh();
            StatusText.Text = repository.HasNote
                ? $"已保存“{repository.FullName}”的收藏备注。"
                : $"已删除“{repository.FullName}”的收藏备注。";
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
    }

    private async void ExportFavoriteNotesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = await _favorites.BuildNotesTextAsync();
            if (string.IsNullOrWhiteSpace(content))
            {
                MessageBox.Show(
                    this,
                    "当前没有可导出的收藏备注。",
                    "导出备注",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "导出收藏备注",
                Filter = "TXT 文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
                DefaultExt = ".txt",
                AddExtension = true,
                InitialDirectory = AppContext.BaseDirectory,
                FileName = $"收藏备注-{DateTime.Now:yyyyMMdd-HHmmss}.txt"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            await File.WriteAllTextAsync(
                dialog.FileName,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            StatusText.Text = $"收藏备注已导出：{dialog.FileName}";
        }
        catch (Exception exception)
        {
            ShowFavoriteError(exception);
        }
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
        var searchViewActive = ReferenceEquals(
            CombinedContentTabs.SelectedItem,
            CombinedSearchResultsTab);
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
            .OrderBy(repository => repository.SortOrder)
            .ThenByDescending(repository => repository.SavedAt)
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
            "目录名称会显示在收藏中。")
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
