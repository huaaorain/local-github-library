using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GitHubTopicReports.Services;

public sealed class LlmPromptService
{
    public const string FullNamePlaceholder = "{{FullName}}";
    public const string DescriptionPlaceholder = "{{Description}}";
    public const string TopicsPlaceholder = "{{Topics}}";
    public const string ReadmeSummaryPlaceholder = "{{ReadmeSummary}}";

    public const string DefaultPrompt =
        """
        你是一名严谨的开源技术编辑。请忠实依据提供的仓库信息，先翻译，再总结。
        除代码、命令、链接和专有技术名词外，不得输出完整的英文句子，必须使用简体中文。
        不得编造输入中不存在的功能、性能数字或兼容性。
        项目名、API、框架名、命令和专有技术名词保留原文。

        仓库：{{FullName}}
        Description：{{Description}}
        Topics：{{Topics}}

        README 核心内容：
        {{ReadmeSummary}}

        输出格式：
        【中文翻译】
        把上面的 Description 和 README 核心内容翻译成简体中文。
        这一部分禁止照抄英文原句；代码、命令和专有名词除外。

        【中文总结】
        根据上面的中文翻译，用 2–3 句话说明项目解决的问题、主要能力和适用对象。

        再次强调：除专有名词外，全文必须使用简体中文。
        """;

    private const int MaximumPromptLength = 30_000;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly Regex PlaceholderRegex = new(
        @"\{\{[^{}\r\n]+\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex PlaceholderRenderRegex = new(
        @"\{\{(FullName|Description|Topics|ReadmeSummary)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public LlmPromptService(string libraryRoot)
    {
        RootDirectory = Path.Combine(Path.GetFullPath(libraryRoot), "LLM");
        ActivePromptFile = Path.Combine(RootDirectory, "prompt.txt");
    }

    public string RootDirectory { get; }
    public string ActivePromptFile { get; }

    public async Task<string> GetActivePromptAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ActivePromptFile))
        {
            await SaveAsync(DefaultPrompt, cancellationToken);
            return DefaultPrompt;
        }

        var prompt = await File.ReadAllTextAsync(ActivePromptFile, cancellationToken);
        return ValidateAndNormalize(prompt);
    }

    public async Task<string> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                Path.GetExtension(sourcePath),
                ".txt",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("请选择 TXT 格式的 LLM 提示词文件。");
        }

        var prompt = await File.ReadAllTextAsync(sourcePath, cancellationToken);
        await SaveAsync(prompt, cancellationToken);
        return ValidateAndNormalize(prompt);
    }

    public async Task SaveAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var normalized = ValidateAndNormalize(prompt);
        Directory.CreateDirectory(RootDirectory);
        var temporaryPath = ActivePromptFile + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            normalized,
            Utf8NoBom,
            cancellationToken);
        File.Move(temporaryPath, ActivePromptFile, overwrite: true);
    }

    public async Task ExportActiveAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var prompt = await GetActivePromptAsync(cancellationToken);
        Directory.CreateDirectory(
            Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        await File.WriteAllTextAsync(
            destinationPath,
            prompt,
            Utf8NoBom,
            cancellationToken);
    }

    public static string Render(
        string promptTemplate,
        string fullName,
        string description,
        IReadOnlyList<string> topics,
        string readmeSummary)
    {
        return PlaceholderRenderRegex.Replace(
            promptTemplate,
            match => match.Groups[1].Value switch
            {
                "FullName" => fullName,
                "Description" => string.IsNullOrWhiteSpace(description)
                    ? "无"
                    : description,
                "Topics" => topics.Count == 0 ? "无" : string.Join(", ", topics),
                "ReadmeSummary" => string.IsNullOrWhiteSpace(readmeSummary)
                    ? "无可用 README。"
                    : readmeSummary,
                _ => match.Value
            });
    }

    public static string ValidateAndNormalize(string prompt)
    {
        var normalized = (prompt ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length == 0)
        {
            throw new InvalidDataException("LLM 提示词不能为空。");
        }

        if (normalized.Length > MaximumPromptLength)
        {
            throw new InvalidDataException(
                $"LLM 提示词不能超过 {MaximumPromptLength:N0} 个字符。");
        }

        if (!normalized.Contains(FullNamePlaceholder, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"LLM 提示词必须包含 {FullNamePlaceholder} 占位符。");
        }

        foreach (Match match in PlaceholderRegex.Matches(normalized))
        {
            if (match.Value is not (
                    FullNamePlaceholder or
                    DescriptionPlaceholder or
                    TopicsPlaceholder or
                    ReadmeSummaryPlaceholder))
            {
                throw new InvalidDataException(
                    $"LLM 提示词包含未知占位符：{match.Value}");
            }
        }

        return normalized;
    }
}
