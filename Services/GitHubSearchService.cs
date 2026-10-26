using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class GitHubSearchService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, string> _summaryCache =
        new(StringComparer.OrdinalIgnoreCase);

    public GitHubSearchService()
    {
        var handler = new SocketsHttpHandler
        {
            // The app must not inherit broken ALL_PROXY/HTTPS_PROXY values from a
            // terminal or a VPN client. api.github.com is reached directly.
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12
            }
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(35)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GitHubTopicReports/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

        var token = LoadToken();
        if (!string.IsNullOrWhiteSpace(token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }
    }

    public bool HasToken =>
        _httpClient.DefaultRequestHeaders.Authorization is not null;

    public async Task<string> GetProjectSummaryAsync(
        string fullName,
        string fallbackDescription,
        CancellationToken cancellationToken)
    {
        var cacheKey = string.IsNullOrWhiteSpace(fallbackDescription)
            ? fullName
            : $"{fullName}\u001f{fallbackDescription}";
        if (_summaryCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var fallback = string.IsNullOrWhiteSpace(fallbackDescription)
            ? "暂无项目功能简介。"
            : fallbackDescription.Trim();
        var slashIndex = fullName.IndexOf('/');
        if (slashIndex <= 0 || slashIndex >= fullName.Length - 1)
        {
            _summaryCache[cacheKey] = fallback;
            return fallback;
        }

        var owner = Uri.EscapeDataString(fullName[..slashIndex]);
        var repository = Uri.EscapeDataString(fullName[(slashIndex + 1)..]);
        var url = $"https://api.github.com/repos/{owner}/{repository}/readme";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _summaryCache[cacheKey] = fallback;
                return fallback;
            }

            var markdown = await response.Content.ReadAsStringAsync(cancellationToken);
            var summary = ProjectReadmeSummary.Extract(markdown, fallback);
            _summaryCache[cacheKey] = summary;
            return summary;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _summaryCache[cacheKey] = fallback;
            return fallback;
        }
    }

    public async Task<IReadOnlyList<RepositoryRecord>> SearchAsync(
        string? keyword,
        int maxResults,
        CancellationToken cancellationToken)
    {
        maxResults = Math.Clamp(maxResults, 1, 1000);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return await ExecuteSearchAsync(
                "stars:>0 archived:false fork:false",
                maxResults,
                cancellationToken);
        }

        var repositories = await SearchTextAsync(
            keyword.Trim(),
            maxResults,
            cancellationToken);
        if (repositories.Count == 0 && !keyword.Contains(' '))
        {
            repositories = await SearchTopicAsync(
                keyword.Trim(),
                maxResults,
                cancellationToken);
        }

        return repositories;
    }

    private async Task<List<RepositoryRecord>> SearchTextAsync(
        string keyword,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var query = $"{keyword.Trim()} in:name,description,readme archived:false";
        return await ExecuteSearchAsync(query, maxResults, cancellationToken);
    }

    private async Task<List<RepositoryRecord>> SearchTopicAsync(
        string keyword,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var safeTopic = keyword.Trim().ToLowerInvariant().Replace(' ', '-');
        return await ExecuteSearchAsync(
            $"topic:{safeTopic} archived:false",
            maxResults,
            cancellationToken);
    }

    private async Task<List<RepositoryRecord>> ExecuteSearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var collected = new List<RepositoryRecord>();
        var required = Math.Clamp(maxResults, 1, 1000);

        for (var page = 1; collected.Count < required && page <= 10; page++)
        {
            // Keep the request shape stable across different display counts.
            // GitHub's broad-search ordering can vary when per_page changes.
            const int pageSize = 100;
            var url =
                "https://api.github.com/search/repositories" +
                $"?q={Uri.EscapeDataString(query)}" +
                $"&sort=stars&order=desc&per_page={pageSize}&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new GitHubNetworkException(
                    "连接 GitHub 超时，请检查网络后重试。",
                    exception);
            }
            catch (HttpRequestException exception)
            {
                var message = AppDiagnostics.GetUserMessage(exception);
                throw new GitHubNetworkException(message, exception);
            }

            using (response)
            {
                var pageItems = await ReadSearchResponseAsync(response, cancellationToken);
                if (pageItems.Count == 0)
                {
                    break;
                }

                collected.AddRange(pageItems);
                if (pageItems.Count < pageSize)
                {
                    break;
                }
            }
        }

        for (var index = 0; index < collected.Count; index++)
        {
            collected[index].Rank = index + 1;
        }

        return collected.Take(required).ToList();
    }

    private static async Task<List<RepositoryRecord>> ReadSearchResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GitHubApiException(
                BuildErrorMessage(response.StatusCode, body),
                response.StatusCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<RepositorySearchResponse>(
            cancellationToken: cancellationToken);

        return payload?.Items.Select((item, index) => new RepositoryRecord
        {
            Id = item.Id,
            Rank = index + 1,
            FullName = item.FullName,
            HtmlUrl = item.HtmlUrl,
            Description = item.Description?.Trim() ?? "",
            Stars = item.StargazersCount,
            Forks = item.ForksCount,
            OpenIssues = item.OpenIssuesCount,
            Language = item.Language ?? "未知",
            Topics = item.Topics.Order().ToList(),
            UpdatedAt = item.UpdatedAt,
            CreatedAt = item.CreatedAt,
            PushedAt = item.PushedAt,
            License = item.License?.SpdxId is { Length: > 0 } license
                ? license
                : "未知"
        }).ToList() ?? [];
    }

    private static string BuildErrorMessage(HttpStatusCode statusCode, string responseBody)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized =>
                "GitHub Token 无效。请检查 GITHUB_TOKEN 或程序目录下的 github-token.txt。",
            HttpStatusCode.Forbidden or (HttpStatusCode)429 =>
                "GitHub API 请求频率已用完。请稍后重试，或配置 GITHUB_TOKEN 提高限额。",
            _ =>
                $"GitHub API 请求失败：{(int)statusCode} {statusCode}。{TrimResponse(responseBody)}"
        };
    }

    private static string TrimResponse(string value)
    {
        var compact = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 240 ? compact : compact[..240] + "...";
    }

    private static string? LoadToken()
    {
        var environmentToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(environmentToken))
        {
            return environmentToken.Trim();
        }

        var tokenPath = Path.Combine(AppContext.BaseDirectory, "github-token.txt");
        return File.Exists(tokenPath) ? File.ReadAllText(tokenPath).Trim() : null;
    }

    public void Dispose() => _httpClient.Dispose();
}

public sealed class GitHubApiException : Exception
{
    public GitHubApiException(string message, HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}

public sealed class GitHubNetworkException : Exception
{
    public GitHubNetworkException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class ProjectReadmeSummary
{
    private const int MaxSummaryLength = 500;

    private static readonly Regex HtmlCommentRegex = new(
        "<!--.*?-->",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex CodeBlockRegex = new(
        "```.*?```",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex HtmlTagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownImageRegex = new(
        @"!\[[^\]]*\]\([^\)]*\)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownLinkRegex = new(
        @"\[(?<text>[^\]]*)\]\([^\)]+\)",
        RegexOptions.Compiled);
    private static readonly Regex MarkdownReferenceRegex = new(
        @"^\s*\[[^\]]+\]:\s*\S+.*$",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BadgeLineRegex = new(
        @"(shields\.io|badge|img\.shields|github\.com/.*/actions|codecov|travis-ci)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceRegex = new(
        @"[ \t]+",
        RegexOptions.Compiled);

    public static string Extract(string markdown, string fallback)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return fallback;
        }

        var content = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        if (content.StartsWith("---\n", StringComparison.Ordinal))
        {
            var frontMatterEnd = content.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (frontMatterEnd >= 0)
            {
                content = content[(frontMatterEnd + 4)..];
            }
        }

        content = HtmlCommentRegex.Replace(content, "");
        content = CodeBlockRegex.Replace(content, "");
        content = MarkdownImageRegex.Replace(content, "");
        content = MarkdownLinkRegex.Replace(content, "${text}");
        content = MarkdownReferenceRegex.Replace(content, "");
        content = HtmlTagRegex.Replace(content, "");

        var paragraphs = new List<string>();
        foreach (var rawParagraph in Regex.Split(content, @"\n\s*\n"))
        {
            var lines = rawParagraph
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line =>
                    line.Length > 0 &&
                    !line.StartsWith('#') &&
                    !line.StartsWith('|') &&
                    !line.StartsWith("```", StringComparison.Ordinal) &&
                    !line.StartsWith("~~~", StringComparison.Ordinal) &&
                    !line.StartsWith("<", StringComparison.Ordinal) &&
                    !BadgeLineRegex.IsMatch(line))
                .Select(line => line
                    .TrimStart('-', '*', '>', '+', ' ')
                    .Replace("**", "")
                    .Replace("__", "")
                    .Replace("`", ""))
                .Where(line => line.Length > 0)
                .ToList();

            if (lines.Count == 0)
            {
                continue;
            }

            var paragraph = WhitespaceRegex.Replace(string.Join(' ', lines), " ").Trim();
            var lowerParagraph = paragraph.ToLowerInvariant();
            if (paragraph.Length < 60 ||
                paragraph.Equals("Table of Contents", StringComparison.OrdinalIgnoreCase) ||
                paragraph.Equals("Contents", StringComparison.OrdinalIgnoreCase) ||
                paragraph.Equals("目录", StringComparison.Ordinal) ||
                lowerParagraph.StartsWith("what is ", StringComparison.Ordinal) ||
                lowerParagraph.StartsWith("how to use", StringComparison.Ordinal) ||
                lowerParagraph.StartsWith("table of contents", StringComparison.Ordinal) ||
                lowerParagraph.StartsWith("contents", StringComparison.Ordinal) ||
                lowerParagraph.Contains("reporting security issues", StringComparison.Ordinal) ||
                lowerParagraph.Contains("filing issues", StringComparison.Ordinal) ||
                lowerParagraph.Contains("useful links", StringComparison.Ordinal) ||
                paragraph.Count(character => character == '?') >= 2)
            {
                continue;
            }

            paragraphs.Add(paragraph);
            if (paragraph.Length >= 120 || paragraphs.Count >= 2)
            {
                break;
            }
        }

        if (paragraphs.Count == 0)
        {
            return fallback;
        }

        var summary = string.Join("\n\n", paragraphs);
        if (summary.Length <= MaxSummaryLength)
        {
            return summary;
        }

        var shortened = summary[..MaxSummaryLength];
        var sentenceEnd = shortened.LastIndexOfAny(['。', '！', '？', '.', '!', '?']);
        return sentenceEnd >= 180
            ? shortened[..(sentenceEnd + 1)]
            : shortened.TrimEnd() + "...";
    }
}
