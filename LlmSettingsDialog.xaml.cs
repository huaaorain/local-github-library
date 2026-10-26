using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using GitHubTopicReports.Models;
using GitHubTopicReports.Services;

namespace GitHubTopicReports;

public partial class LlmSettingsDialog : Window
{
    private readonly LlmPackageService _packageService;
    private readonly LlmPromptService _promptService;
    private readonly LocalLlmService _localLlm;
    private readonly Func<string> _appVersionProvider;
    private string _loadedPrompt = "";
    private bool _isEditingPrompt;
    private bool _isModelBusy;
    private bool _isPromptBusy;

    public LlmSettingsDialog(
        LlmPackageService packageService,
        LlmPromptService promptService,
        LocalLlmService localLlm,
        Func<string> appVersionProvider)
    {
        _packageService = packageService;
        _promptService = promptService;
        _localLlm = localLlm;
        _appVersionProvider = appVersionProvider;
        InitializeComponent();
        Loaded += LlmSettingsDialog_Loaded;
        Closing += LlmSettingsDialog_Closing;
    }

    public bool PromptChanged { get; private set; }

    private async void LlmSettingsDialog_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshModelStatus();
        await LoadPromptAsync();
    }

    private void RefreshModelStatus()
    {
        if (_localLlm.ActivePackage is not { } package)
        {
            CurrentModelNameText.Text = "尚未导入模型包";
            CurrentModelDetailsText.Text = "基础搜索和收藏功能不受影响。";
            return;
        }

        var manifest = package.Manifest;
        CurrentModelNameText.Text = manifest.DisplayName;
        CurrentModelDetailsText.Text =
            $"量化：{manifest.Quantization}\n" +
            $"模型大小：{FormatBytes(package.ModelSize)}\n" +
            $"安装位置：{package.PackageDirectory}";
    }

    private async Task LoadPromptAsync()
    {
        try
        {
            _loadedPrompt = await _promptService.GetActivePromptAsync();
            PromptTextBox.Text = _loadedPrompt;
            PromptSourceText.Text = $"当前提示词文件：{_promptService.ActivePromptFile}";
            PromptStatusText.Text = "";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("读取 LLM 提示词", exception);
            PromptStatusText.Text =
                $"提示词读取失败：{AppDiagnostics.GetUserMessage(exception)}\n{logPath}";
        }
    }

    private async void ImportLlmPackageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入本地 LLM 模型包",
            Filter = "Local Github Library AI 包 (*.ghllm)|*.ghllm|ZIP 文件 (*.zip)|*.zip",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetBusyState(true, "正在验证并导入 LLM 模型包...");
        try
        {
            _localLlm.ReleaseModel();
            var result = await _packageService.ImportAsync(
                dialog.FileName,
                _appVersionProvider(),
                CancellationToken.None);
            await _localLlm.RefreshPackageAsync();
            RefreshModelStatus();
            ModelStatusText.Text = result.ReplacedExistingPackage
                ? "导入完成，已替换原有的同名模型包。"
                : "导入完成。";
            DialogGlobalStatusText.Text =
                $"当前模型：{result.Package.Manifest.DisplayName}";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("导入本地 LLM", exception);
            ModelStatusText.Text =
                $"模型导入失败：{AppDiagnostics.GetUserMessage(exception)}\n{logPath}";
        }
        finally
        {
            SetBusyState(false, "");
        }
    }

    private async void ImportPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 LLM 提示词",
            Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetPromptBusyState(true, "正在导入提示词...");
        try
        {
            _loadedPrompt = await _promptService.ImportAsync(dialog.FileName);
            PromptTextBox.Text = _loadedPrompt;
            _isEditingPrompt = false;
            SetPromptReadOnlyMode();
            _localLlm.ReleaseModel();
            PromptChanged = true;
            PromptStatusText.Text = "提示词已导入并保存。下一次生成时生效。";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("导入 LLM 提示词", exception);
            PromptStatusText.Text =
                $"提示词导入失败：{AppDiagnostics.GetUserMessage(exception)}\n{logPath}";
        }
        finally
        {
            SetPromptBusyState(false, "");
        }
    }

    private async void ExportPromptButton_Click(object sender, RoutedEventArgs e)
    {
        var destinationPath = CreatePromptExportPath();
        SetPromptBusyState(true, "正在导出提示词...");
        try
        {
            await _promptService.ExportActiveAsync(destinationPath);
            PromptStatusText.Text = $"提示词已导出：{destinationPath}";
            MessageBox.Show(
                this,
                $"提示词已导出到：\n{destinationPath}",
                "提示词导出完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("导出 LLM 提示词", exception);
            PromptStatusText.Text =
                $"提示词导出失败：{AppDiagnostics.GetUserMessage(exception)}\n{logPath}";
        }
        finally
        {
            SetPromptBusyState(false, "");
        }
    }

    private void EditPromptButton_Click(object sender, RoutedEventArgs e)
    {
        _isEditingPrompt = true;
        PromptStatusText.Text = "提示词编辑中。完成后请点击右上角“保存”。";
        UpdateControlStates();
        PromptTextBox.Focus();
        PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
    }

    private async void SavePromptButton_Click(object sender, RoutedEventArgs e)
    {
        SetPromptBusyState(true, "正在保存提示词...");
        try
        {
            await _promptService.SaveAsync(PromptTextBox.Text);
            _loadedPrompt = await _promptService.GetActivePromptAsync();
            PromptTextBox.Text = _loadedPrompt;
            _isEditingPrompt = false;
            SetPromptReadOnlyMode();
            _localLlm.ReleaseModel();
            PromptChanged = true;
            PromptStatusText.Text = "提示词已保存。下一次生成时生效。";
        }
        catch (Exception exception)
        {
            var logPath = AppDiagnostics.LogException("保存 LLM 提示词", exception);
            PromptStatusText.Text =
                $"提示词保存失败：{AppDiagnostics.GetUserMessage(exception)}\n{logPath}";
        }
        finally
        {
            SetPromptBusyState(false, "");
        }
    }

    private void SetPromptReadOnlyMode()
    {
        _isEditingPrompt = false;
        UpdateControlStates();
    }

    private void SetBusyState(bool busy, string status)
    {
        _isModelBusy = busy;
        UpdateControlStates();
        DialogGlobalStatusText.Text = status;
    }

    private void SetPromptBusyState(bool busy, string status)
    {
        _isPromptBusy = busy;
        UpdateControlStates();
        PromptStatusText.Text = status;
    }

    private void UpdateControlStates()
    {
        var idle = !_isModelBusy && !_isPromptBusy;
        ImportLlmPackageButton.IsEnabled = idle && !_isEditingPrompt;
        EditPromptButton.IsEnabled = idle && !_isEditingPrompt;
        SavePromptButton.IsEnabled = idle && _isEditingPrompt;
        ImportPromptButton.IsEnabled = idle && !_isEditingPrompt;
        ExportPromptButton.IsEnabled = idle && !_isEditingPrompt;
        PromptTextBox.IsReadOnly =
            _isModelBusy || _isPromptBusy || !_isEditingPrompt;
        EditPromptButton.Visibility = _isEditingPrompt
            ? Visibility.Collapsed
            : Visibility.Visible;
        SavePromptButton.Visibility = _isEditingPrompt
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string CreatePromptExportPath()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var basePath = Path.Combine(
            AppContext.BaseDirectory,
            $"LLM提示词-{timestamp}");
        var candidate = basePath + ".txt";
        var suffix = 1;
        while (File.Exists(candidate))
        {
            candidate = $"{basePath}-{suffix++}.txt";
        }

        return candidate;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }

    private void LlmSettingsDialog_Closing(object? sender, CancelEventArgs e)
    {
        if (!_isEditingPrompt ||
            PromptTextBox.Text == _loadedPrompt)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            "提示词还有未保存的修改。是否放弃修改并关闭？",
            "放弃未保存的提示词",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
