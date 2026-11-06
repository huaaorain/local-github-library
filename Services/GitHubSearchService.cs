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
    private readonly SearchVocabulary _searchVocabulary;
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

        _searchVocabulary = new SearchVocabulary(
            Path.Combine(
                AppContext.BaseDirectory,
                "GitHubReports",
                "Data",
                "search-vocabulary.txt"));
    }

    public bool HasToken =>
        _httpClient.DefaultRequestHeaders.Authorization is not null;
    public SearchQueryCorrection? LastQueryCorrection { get; private set; }

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
        LastQueryCorrection = null;
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return await ExecuteSearchAsync(
                "stars:>0 archived:false fork:false",
                maxResults,
                cancellationToken,
                sortByStars: true);
        }

        var normalizedKeyword = keyword.Trim();
        var exactResults = await SearchMatchingAsync(
            normalizedKeyword,
            maxResults,
            cancellationToken);
        if (exactResults.Count >= Math.Min(maxResults, 3))
        {
            return exactResults;
        }

        var correction = SearchQueryCorrector.Correct(
            normalizedKeyword,
            _searchVocabulary.Terms);
        if (correction is null)
        {
            return exactResults;
        }

        var correctedResults = await SearchMatchingAsync(
            correction.CorrectedQuery,
            maxResults,
            cancellationToken);
        if (correctedResults.Count == 0)
        {
            return exactResults;
        }

        LastQueryCorrection = correction;
        var exactIds = exactResults
            .Select(repository => repository.Id)
            .ToHashSet();
        var merged = exactResults
            .Concat(correctedResults.Where(repository => !exactIds.Contains(repository.Id)))
            .Take(maxResults)
            .ToList();
        for (var index = 0; index < merged.Count; index++)
        {
            merged[index].Rank = index + 1;
        }

        return merged;
    }

    private async Task<List<RepositoryRecord>> SearchMatchingAsync(
        string normalizedKeyword,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var topicCandidateLimit = Math.Clamp(maxResults * 2, 100, 1000);
        var metadataCandidateLimit = Math.Clamp(maxResults * 6, 200, 800);
        var topicResults = await SearchTopicAsync(
            normalizedKeyword,
            topicCandidateLimit,
            cancellationToken);

        var candidates = new Dictionary<long, SearchCandidate>();
        foreach (var repository in topicResults)
        {
            candidates[repository.Id] = new SearchCandidate
            {
                Repository = repository,
                MatchedTopic = true,
                TopicRank = repository.Rank
            };
        }

        var metadataResults = await SearchMetadataAsync(
            normalizedKeyword,
            metadataCandidateLimit,
            cancellationToken);
        foreach (var repository in metadataResults)
        {
            if (candidates.TryGetValue(repository.Id, out var candidate))
            {
                candidate.MatchedMetadata = true;
                candidate.MetadataRank = repository.Rank;
            }
            else
            {
                candidates[repository.Id] = new SearchCandidate
                {
                    Repository = repository,
                    MatchedMetadata = true,
                    MetadataRank = repository.Rank
                };
            }
        }

        _searchVocabulary.Learn(candidates.Values.Select(candidate => candidate.Repository));
        _searchVocabulary.SaveIfNeeded();

        var scoredCandidates = candidates.Values
            .Select(candidate => new ScoredSearchCandidate(
                candidate,
                SearchRelevanceScorer.Score(candidate, normalizedKeyword)))
            .Where(result => result.Score >= SearchRelevanceScorer.MinimumScore)
            .ToList();

        var selected = SearchRelevanceScorer
            .SelectDiverse(scoredCandidates, normalizedKeyword, maxResults);
        for (var index = 0; index < selected.Count; index++)
        {
            selected[index].Rank = index + 1;
        }

        return selected.ToList();
    }

    private async Task<List<RepositoryRecord>> SearchMetadataAsync(
        string keyword,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var query =
            $"{keyword.Trim()} in:name,description archived:false fork:false";
        return await ExecuteSearchAsync(
            query,
            maxResults,
            cancellationToken,
            sortByStars: false);
    }

    private async Task<List<RepositoryRecord>> SearchTopicAsync(
        string keyword,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var safeTopic = SearchRelevanceScorer.NormalizeTopic(keyword);
        if (string.IsNullOrWhiteSpace(safeTopic))
        {
            return [];
        }

        return await ExecuteSearchAsync(
            $"topic:{safeTopic} archived:false fork:false",
            maxResults,
            cancellationToken,
            sortByStars: true);
    }

    private async Task<List<RepositoryRecord>> ExecuteSearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken,
        bool sortByStars)
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
                (sortByStars ? "&sort=stars&order=desc" : "") +
                $"&per_page={pageSize}&page={page}";

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

internal sealed class SearchCandidate
{
    public required RepositoryRecord Repository { get; init; }
    public bool MatchedMetadata { get; set; }
    public bool MatchedTopic { get; set; }
    public int? MetadataRank { get; set; }
    public int? TopicRank { get; set; }
}

internal sealed record ScoredSearchCandidate(
    SearchCandidate Candidate,
    int Score);

internal static class SearchRelevanceScorer
{
    public const int MinimumScore = 35;

    private static readonly char[] TokenSeparators =
    [
        ' ',
        '\t',
        '\r',
        '\n',
        '-',
        '_',
        '/',
        '\\',
        '.',
        ',',
        ':',
        ';',
        '|',
        '(',
        ')',
        '[',
        ']'
    ];

    public static int Score(SearchCandidate candidate, string keyword)
    {
        var repository = candidate.Repository;
        var normalizedKeyword = keyword.Trim();
        var queryTokens = Tokenize(normalizedKeyword);
        var topicSlug = NormalizeTopic(normalizedKeyword);
        var repositoryName = repository.FullName
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? repository.FullName;
        var repositoryNameContainsKeyword =
            ContainsWholeTerm(repositoryName, normalizedKeyword);
        var repositoryNameContainsAllTokens =
            queryTokens.Count > 0 &&
            queryTokens.All(token => ContainsWholeTerm(repositoryName, token));
        var descriptionContainsKeyword =
            ContainsWholeTerm(repository.Description, normalizedKeyword);
        var descriptionContainsAllTokens =
            queryTokens.Count > 0 &&
            queryTokens.All(token => ContainsWholeTerm(repository.Description, token));
        var hasDirectTextMatch =
            repositoryNameContainsKeyword ||
            ContainsWholeTerm(repository.FullName, normalizedKeyword) ||
            repositoryNameContainsAllTokens ||
            descriptionContainsKeyword ||
            descriptionContainsAllTokens;

        var score = 0;
        if (candidate.MatchedMetadata)
        {
            score += 8;
        }

        if (candidate.MetadataRank is > 0 and var metadataRank)
        {
            // GitHub's native best-match order is a useful generic signal. It
            // keeps canonical projects near the top without special-casing any
            // repository. Only the leading matches receive a bonus so that a
            // large ecosystem of similarly named add-ons cannot bury a precise
            // match that GitHub ranks slightly later.
            score += metadataRank switch
            {
                1 => 100,
                2 => 85,
                3 => 70,
                4 => 60,
                5 => 50,
                <= 10 => 30,
                <= 25 => 15,
                _ => 0
            };
        }

        if (candidate.MatchedTopic && !hasDirectTextMatch)
        {
            score += 18;
        }

        if (repositoryNameContainsKeyword)
        {
            score += 100;
            if (queryTokens.Count > 1 &&
                repositoryName.StartsWith(
                    normalizedKeyword,
                    StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }
        else if (ContainsWholeTerm(repository.FullName, normalizedKeyword))
        {
            score += 58;
        }
        else if (queryTokens.Count > 0 &&
                 queryTokens.All(token => ContainsWholeTerm(repository.FullName, token)))
        {
            score += 42;
        }

        var exactTopicMatch = !string.IsNullOrWhiteSpace(topicSlug) &&
                              repository.Topics.Any(topic =>
                                  string.Equals(
                                      NormalizeTopic(topic),
                                      topicSlug,
                                      StringComparison.OrdinalIgnoreCase));
        if (exactTopicMatch && !hasDirectTextMatch)
        {
            score += 55;
        }
        else if (!hasDirectTextMatch &&
                 queryTokens.Count > 0 &&
                 queryTokens.All(token =>
                     repository.Topics.Any(topic =>
                         ContainsWholeTerm(topic, token))))
        {
            score += 32;
        }

        if (!hasDirectTextMatch && candidate.TopicRank is > 0 and var topicRank)
        {
            // Topic candidates are ordered by Stars, giving a stable popularity
            // signal for domain-only matches such as "data visualization".
            score += topicRank switch
            {
                1 => 60,
                2 => 50,
                3 => 40,
                4 => 30,
                5 => 25,
                <= 10 => 15,
                <= 25 => 5,
                _ => 0
            };
        }

        if (descriptionContainsKeyword)
        {
            score += 35;
        }
        else if (descriptionContainsAllTokens)
        {
            score += 25;
        }
        else if (queryTokens.Any(token =>
                     ContainsWholeTerm(repository.Description, token)))
        {
            score += 8;
        }

        score += Math.Min(
            15,
            (int)Math.Round(Math.Log10(repository.Stars + 1d) * 2.5));
        return score;
    }

    public static IReadOnlyList<RepositoryRecord> SelectDiverse(
        IReadOnlyList<ScoredSearchCandidate> candidates,
        string keyword,
        int limit)
    {
        var remaining = candidates.ToList();
        var selected = new List<RepositoryRecord>(Math.Min(limit, remaining.Count));
        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var queryTokens = Tokenize(keyword).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var contentType in remaining
                     .Select(candidate => GetContentType(candidate.Candidate.Repository))
                     .Where(type => type is not null)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Take(limit)
                     .ToList())
        {
            var representative = remaining
                .Where(candidate =>
                    string.Equals(
                        GetContentType(candidate.Candidate.Repository),
                        contentType,
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(candidate => candidate.Score)
                .ThenByDescending(candidate => candidate.Candidate.Repository.Stars)
                .First();
            remaining.Remove(representative);
            selected.Add(representative.Candidate.Repository);
            var category = GetDiversityCategory(
                representative.Candidate.Repository,
                queryTokens);
            if (category is not null)
            {
                categoryCounts[category] =
                    categoryCounts.GetValueOrDefault(category) + 1;
            }
        }

        while (selected.Count < limit && remaining.Count > 0)
        {
            var bestIndex = 0;
            var bestAdjustedScore = double.NegativeInfinity;
            for (var index = 0; index < remaining.Count; index++)
            {
                var candidate = remaining[index];
                var category = GetDiversityCategory(
                    candidate.Candidate.Repository,
                    queryTokens);
                var repeatedCount = category is null
                    ? 0
                    : categoryCounts.GetValueOrDefault(category);
                var adjustedScore = candidate.Score
                                    - repeatedCount * 35d
                                    + Math.Log10(
                                        candidate.Candidate.Repository.Stars + 1d) * 5d;
                if (adjustedScore > bestAdjustedScore)
                {
                    bestAdjustedScore = adjustedScore;
                    bestIndex = index;
                }
            }

            var best = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            selected.Add(best.Candidate.Repository);
            var bestCategory = GetDiversityCategory(
                best.Candidate.Repository,
                queryTokens);
            if (bestCategory is not null)
            {
                categoryCounts[bestCategory] =
                    categoryCounts.GetValueOrDefault(bestCategory) + 1;
            }
        }

        return selected;
    }

    public static string NormalizeTopic(string value)
    {
        var topic = Regex.Replace(
            value.Trim().ToLowerInvariant(),
            @"\s+",
            "-",
            RegexOptions.CultureInvariant);
        topic = topic.Trim('-');
        return Regex.IsMatch(
            topic,
            @"^[a-z0-9][a-z0-9-]*$",
            RegexOptions.CultureInvariant)
            ? topic
            : "";
    }

    private static List<string> Tokenize(string value) =>
        value.ToLowerInvariant()
            .Split(
                TokenSeparators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? GetDiversityCategory(
        RepositoryRecord repository,
        IReadOnlySet<string> queryTokens)
    {
        var repositoryName = repository.FullName
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? repository.FullName;
        var searchable = $"{repositoryName} {repository.Description}";
        string[] learningMarkers =
        [
            "awesome",
            "cookbook",
            "tutorial",
            "course",
            "guide",
            "handbook",
            "learn",
            "learning",
            "example",
            "入门",
            "教程",
            "指南",
            "手册",
            "知识"
        ];
        if (learningMarkers.Any(marker =>
                searchable.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            return "learning-resources";
        }

        var repositoryTokens = Tokenize(repositoryName);
        if (repositoryTokens.Count > 0 &&
            repositoryTokens.All(queryTokens.Contains))
        {
            return "query-only-name";
        }

        return repositoryTokens
            .FirstOrDefault(token =>
                token.Length >= 3 &&
                !queryTokens.Contains(token));
    }

    private static string? GetContentType(RepositoryRecord repository)
    {
        var text =
            $"{repository.FullName} {repository.Description}"
                .ToLowerInvariant();
        if (text.Contains("awesome", StringComparison.Ordinal) ||
            text.Contains("curated list", StringComparison.Ordinal))
        {
            return "awesome-list";
        }

        string[] tutorialMarkers =
        [
            "cookbook",
            "tutorial",
            "guide",
            "handbook",
            "course",
            "learning",
            "入门",
            "教程",
            "指南",
            "手册"
        ];
        return tutorialMarkers.Any(marker =>
            text.Contains(marker, StringComparison.Ordinal))
            ? "tutorial"
            : null;
    }

    private static bool ContainsWholeTerm(string? text, string keyword)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keyword))
        {
            return false;
        }

        if (Regex.IsMatch(keyword, @"[\u3400-\u9fff]", RegexOptions.CultureInvariant))
        {
            return text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }

        var pattern =
            $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(keyword.Trim())}" +
            @"(?![\p{L}\p{N}_])";
        return Regex.IsMatch(
            text,
            pattern,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
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
    private static readonly Regex SentenceEndRegex = new(
        @"[.!?。！？](?:\s|$)",
        RegexOptions.Compiled);
    private static readonly string[] LanguageNames =
    [
        "English",
        "Chinese",
        "Traditional Chinese",
        "Japanese",
        "Korean",
        "German",
        "Spanish",
        "French",
        "Ukrainian",
        "Portuguese",
        "Arabic",
        "Persian",
        "Indonesian",
        "Italian",
        "Greek",
        "Malayalam",
        "Urdu",
        "Romanian",
        "简体中文",
        "繁體中文",
        "日本語",
        "한국어",
        "Türkçe",
        "Русский",
        "Tiếng Việt",
        "ไทย",
        "Deutsch",
        "Español",
        "Français",
        "Українська",
        "Polski",
        "Português",
        "العربية",
        "فارسی",
        "Bahasa Indonesia",
        "Italiano",
        "Ελληνικά",
        "മലയാളം",
        "Roman Urdu",
        "Română"
    ];

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
                IsNavigationOrLanguageList(paragraph) ||
                paragraph.Equals("Table of Contents", StringComparison.OrdinalIgnoreCase) ||
                paragraph.Equals("Contents", StringComparison.OrdinalIgnoreCase) ||
                paragraph.Equals("目录", StringComparison.Ordinal) ||
                lowerParagraph.Contains("[!important]", StringComparison.Ordinal) ||
                lowerParagraph.Contains("[!warning]", StringComparison.Ordinal) ||
                lowerParagraph.Contains("[!caution]", StringComparison.Ordinal) ||
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

    private static bool IsNavigationOrLanguageList(string paragraph)
    {
        var languageCount = LanguageNames.Count(
            language => paragraph.Contains(language, StringComparison.OrdinalIgnoreCase));
        var separatorCount = paragraph.Count(
            character => character is '·' or '•' or '|' or '｜');
        var slashSeparatorCount = Regex.Matches(paragraph, @"\s/\s").Count;
        separatorCount += slashSeparatorCount;
        var sentencePunctuationCount = SentenceEndRegex.Matches(paragraph).Count;

        if (languageCount >= 3 && paragraph.Length < 600)
        {
            return true;
        }

        if (languageCount >= 2 &&
            separatorCount >= 1 &&
            sentencePunctuationCount == 0)
        {
            return true;
        }

        if (separatorCount < 2)
        {
            return false;
        }

        return sentencePunctuationCount == 0;
    }
}
