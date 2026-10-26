using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using GitHubTopicReports.Models;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace GitHubTopicReports.Services;

public sealed class LocalLlmService : IDisposable
{
    private const string SystemPromptGuard =
        "你只负责执行用户消息中的任务。"
        + "只输出任务要求的最终结果，从结果标题或正文开始。"
        + "禁止复述、引用、解释或续写提示词、规则、字段名称和输出格式说明。"
        + "禁止输出“输出格式：”“把上面的 Description”“根据上面的中文翻译”"
        + "或“再次强调”等提示词内容。";

    private readonly LlmPackageService _packageService;
    private readonly LlmPromptService _promptService;
    private readonly LlmSummaryCacheService _summaryCache;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private static readonly object NativeRuntimeLock = new();
    private static bool _nativeRuntimeConfigured;
    private InstalledLlmPackage? _package;
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private string? _loadedPackageId;

    public LocalLlmService(
        LlmPackageService packageService,
        LlmPromptService promptService)
    {
        _packageService = packageService;
        _promptService = promptService;
        _summaryCache = new LlmSummaryCacheService(packageService.RootDirectory);
    }

    public bool IsAvailable => _package is not null;
    public InstalledLlmPackage? ActivePackage => _package;

    public async Task RefreshPackageAsync(CancellationToken cancellationToken = default)
    {
        var package = await _packageService.GetActivePackageAsync(cancellationToken);
        if (package?.Manifest.PackageId == _loadedPackageId)
        {
            _package = package;
            return;
        }

        DisposeModel();
        _package = package;
    }

    public void ReleaseModel() => DisposeModel();

    public async IAsyncEnumerable<string> GenerateProjectInsightAsync(
        string fullName,
        string description,
        IReadOnlyList<string> topics,
        string readmeSummary,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var package = _package
            ?? throw new InvalidOperationException("尚未安装本地 LLM 模型包。");
        var promptTemplate = await _promptService.GetActivePromptAsync(cancellationToken);
        var cacheKey = LlmSummaryCacheService.CreateKey(
            package.Manifest.PackageId,
            promptTemplate,
            fullName,
            description,
            topics,
            readmeSummary);
        var cached = await _summaryCache.GetAsync(cacheKey, cancellationToken);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            yield return cached;
            yield break;
        }

        await _inferenceLock.WaitAsync(cancellationToken);
        var generatedText = new StringBuilder();
        try
        {
            await EnsureModelLoadedAsync(package, cancellationToken);
            var prompt = LlmPromptService.Render(
                promptTemplate,
                fullName,
                description,
                topics,
                readmeSummary);
            var sampling = new DefaultSamplingPipeline
            {
                Temperature = 0.2f,
                TopP = 0.85f,
                TopK = 40,
                RepeatPenalty = 1.08f,
                Seed = 42
            };
            var inference = new InferenceParams
            {
                MaxTokens = 520,
                AntiPrompts = ["输出格式：", "把上面的 Description"],
                SamplingPipeline = sampling
            };

            await foreach (var token in _executor!
                               .InferAsync(prompt, inference, cancellationToken)
                               .ConfigureAwait(false))
            {
                generatedText.Append(token);
                yield return token;
            }
        }
        finally
        {
            _inferenceLock.Release();
        }

        if (!cancellationToken.IsCancellationRequested &&
            generatedText.Length > 0)
        {
            await _summaryCache.SetAsync(
                cacheKey,
                generatedText.ToString(),
                cancellationToken);
        }
    }

    private async Task EnsureModelLoadedAsync(
        InstalledLlmPackage package,
        CancellationToken cancellationToken)
    {
        if (_executor is not null && _loadedPackageId == package.Manifest.PackageId)
        {
            return;
        }

        DisposeModel();
        ConfigureNativeRuntime();
        var threads = package.Manifest.RecommendedThreads > 0
            ? package.Manifest.RecommendedThreads
            : Math.Clamp(Environment.ProcessorCount - 1, 2, 8);
        var parameters = new ModelParams(package.ModelPath)
        {
            ContextSize = (uint)Math.Clamp(package.Manifest.ContextSize, 1024, 32768),
            GpuLayerCount = 0,
            Threads = threads,
            BatchThreads = threads,
            BatchSize = 256,
            UBatchSize = 256
        };

        _weights = await LLamaWeights.LoadFromFileAsync(parameters, cancellationToken)
            .ConfigureAwait(false);
        _executor = new StatelessExecutor(_weights, parameters)
        {
            ApplyTemplate = true,
            SystemMessage = SystemPromptGuard
        };
        _loadedPackageId = package.Manifest.PackageId;
    }

    private static void ConfigureNativeRuntime()
    {
        lock (NativeRuntimeLock)
        {
            if (_nativeRuntimeConfigured || NativeLibraryConfig.LLama.LibraryHasLoaded)
            {
                return;
            }

            var runtimeIdentifier = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "win-arm64"
                : "win-x64";
            var nativeDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                runtimeIdentifier,
                "native");
            if (!Directory.Exists(nativeDirectory))
            {
                throw new DirectoryNotFoundException(
                    "本地 LLM 运行库缺失。请确认程序目录中存在 "
                    + $@"runtimes\{runtimeIdentifier}\native，且不要只移动 EXE 文件。");
            }

            NativeLibraryConfig.All
                .WithCuda(false)
                .WithVulkan(false)
                .WithAutoFallback(true)
                .WithSearchDirectory(AppContext.BaseDirectory);
            _nativeRuntimeConfigured = true;
        }
    }

    private void DisposeModel()
    {
        _executor?.Context.Dispose();
        _weights?.Dispose();
        _executor = null;
        _weights = null;
        _loadedPackageId = null;
    }

    public void Dispose()
    {
        DisposeModel();
        _inferenceLock.Dispose();
    }
}
