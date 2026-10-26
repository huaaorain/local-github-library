using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class GlobalLeaderboardService : IDisposable
{
    private const string SourceZipUrl =
        "https://codeload.github.com/OpenGithubs/github-daily-rank/zip/refs/heads/main";

    private static readonly Regex TableRowRegex = new(
        @"^\|\s*\d+\s*\|\s*\[(?<name>[^\]]+)\]\((?<url>https://github\.com/[^\)]+)\)\|\s*(?<stars>[^|]+?)\s*\|\s*(?:🔺\s*)?(?<growth>[\d,]+)\s*\|?",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex DescriptionRegex = new(
        @"📝\s*项目描述[：:]\s*(?<description>[^\r\n]*)",
        RegexOptions.Compiled);

    private static readonly Regex CreatedRegex = new(
        @"📅\s*开源时间[：:]\s*(?<date>\d{4}-\d{2}-\d{2})",
        RegexOptions.Compiled);

    private static readonly Regex DetailSectionRegex = new(
        @"<h3[^>]*>\s*\d+\.\s*(?<url>https://github\.com/[^<\s]+).*?</h3>(?<body>.*?)(?=<h3|$)",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private readonly HttpClient _httpClient;
    private readonly string _cacheZipPath;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private List<DailySnapshot>? _cachedSnapshots;
    private DateTime _cachedZipWriteTime;

    public GlobalLeaderboardService(string libraryRoot)
    {
        var dataDirectory = Path.Combine(Path.GetFullPath(libraryRoot), "Data");
        Directory.CreateDirectory(dataDirectory);
        _cacheZipPath = Path.Combine(dataDirectory, "global-daily-ranks.zip");

        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            AutomaticDecompression = DecompressionMethods.All,
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12
            }
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GitHubTopicReports/1.2");
    }

    public async Task<GlobalLeaderboardResult> BuildAsync(
        GlobalLeaderboardPeriodOption period,
        int maxResults,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            var cacheStatus = await EnsureSourceArchiveAsync(forceRefresh, cancellationToken);
            var snapshots = await LoadSnapshotsAsync(cancellationToken);
            if (snapshots.Count == 0)
            {
                throw new InvalidDataException("每日总榜数据为空或格式无法识别。");
            }

            var latestDate = snapshots.Max(snapshot => snapshot.Date);
            var earliestDate = latestDate.AddDays(-(Math.Max(period.Days, 1) - 1));
            var selectedSnapshots = snapshots
                .Where(snapshot => snapshot.Date >= earliestDate && snapshot.Date <= latestDate)
                .OrderBy(snapshot => snapshot.Date)
                .ToList();

            var aggregates = new Dictionary<string, AggregateEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var snapshot in selectedSnapshots)
            {
                foreach (var row in snapshot.Rows)
                {
                    if (!aggregates.TryGetValue(row.FullName, out var aggregate))
                    {
                        aggregate = new AggregateEntry
                        {
                            FullName = row.FullName,
                            HtmlUrl = row.HtmlUrl,
                            FirstSeenDate = snapshot.Date
                        };
                        aggregates[row.FullName] = aggregate;
                    }

                    aggregate.StarIncrease += row.StarIncrease;
                    aggregate.AppearanceDays++;
                    aggregate.LastSeenDate = snapshot.Date;
                    aggregate.CurrentStars = row.CurrentStars;
                    aggregate.CreatedAt = row.CreatedAt ?? aggregate.CreatedAt;
                    if (!string.IsNullOrWhiteSpace(row.Description))
                    {
                        aggregate.Description = row.Description;
                    }
                }
            }

            var entries = aggregates.Values
                .OrderByDescending(entry => entry.StarIncrease)
                .ThenByDescending(entry => entry.CurrentStars)
                .Take(Math.Clamp(maxResults, 1, 1000))
                .Select((entry, index) => new GlobalLeaderboardEntry
                {
                    Rank = index + 1,
                    FullName = entry.FullName,
                    HtmlUrl = entry.HtmlUrl,
                    Description = entry.Description,
                    CurrentStars = entry.CurrentStars,
                    StarIncrease = entry.StarIncrease,
                    AppearanceDays = entry.AppearanceDays,
                    FirstSeenDate = entry.FirstSeenDate,
                    LastSeenDate = entry.LastSeenDate,
                    CreatedAt = entry.CreatedAt
                })
                .ToList();

            return new GlobalLeaderboardResult
            {
                Mode = GlobalLeaderboardMode.Increase,
                PeriodTitle = period.Name,
                PeriodDays = period.Days,
                LatestDataDate = latestDate,
                StartDataDate = selectedSnapshots.Count == 0 ? latestDate : selectedSnapshots[0].Date,
                SourceFileCount = selectedSnapshots.Count,
                CacheStatus = cacheStatus,
                SourceNote =
                    "数据源：OpenGithubs GitHub Daily Rank。按每日飙升榜中的星标增量汇总；" +
                    "项目未进入日榜的日期不会计入该项目的增长。",
                GeneratedAt = DateTimeOffset.Now,
                Entries = entries
            };
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<string> EnsureSourceArchiveAsync(
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var cacheExists = File.Exists(_cacheZipPath);
        var needsRefresh = forceRefresh ||
                           !cacheExists ||
                           DateTime.UtcNow - File.GetLastWriteTimeUtc(_cacheZipPath) > TimeSpan.FromHours(12);
        if (!needsRefresh)
        {
            return "使用本地缓存";
        }

        var temporaryPath = _cacheZipPath + ".tmp";
        try
        {
            using var response = await _httpClient.GetAsync(
                SourceZipUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            File.Move(temporaryPath, _cacheZipPath, overwrite: true);
            _cachedSnapshots = null;
            return "已从 OpenGithubs 更新";
        }
        catch (Exception exception) when (cacheExists)
        {
            AppDiagnostics.LogException("更新全站总榜缓存", exception);
            TryDelete(temporaryPath);
            return $"更新失败，使用本地缓存：{AppDiagnostics.GetUserMessage(exception)}";
        }
    }

    private async Task<List<DailySnapshot>> LoadSnapshotsAsync(CancellationToken cancellationToken)
    {
        var archiveWriteTime = File.GetLastWriteTimeUtc(_cacheZipPath);
        if (_cachedSnapshots is not null && _cachedZipWriteTime == archiveWriteTime)
        {
            return _cachedSnapshots;
        }

        var snapshots = new List<DailySnapshot>();
        await using var file = File.OpenRead(_cacheZipPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(entry.Name);
            if (!DateTime.TryParseExact(
                    fileName,
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                continue;
            }

            await using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var markdown = await reader.ReadToEndAsync(cancellationToken);
            var snapshot = ParseSnapshot(DateOnly.FromDateTime(date), markdown);
            if (snapshot.Rows.Count > 0)
            {
                snapshots.Add(snapshot);
            }
        }

        _cachedSnapshots = snapshots.OrderBy(snapshot => snapshot.Date).ToList();
        _cachedZipWriteTime = archiveWriteTime;
        return _cachedSnapshots;
    }

    private static DailySnapshot ParseSnapshot(DateOnly date, string markdown)
    {
        var details = ParseDetails(markdown);
        var rows = new List<DailyRankRow>();

        foreach (Match match in TableRowRegex.Matches(markdown))
        {
            var fullName = match.Groups["name"].Value.Trim();
            if (!details.TryGetValue(fullName, out var detail))
            {
                detail = new DailyRankDetail();
            }

            rows.Add(new DailyRankRow
            {
                FullName = fullName,
                HtmlUrl = match.Groups["url"].Value.Trim(),
                CurrentStars = ParseCompactNumber(match.Groups["stars"].Value),
                StarIncrease = ParseInteger(match.Groups["growth"].Value),
                Description = detail.Description,
                CreatedAt = detail.CreatedAt
            });
        }

        return new DailySnapshot
        {
            Date = date,
            Rows = rows
        };
    }

    private static Dictionary<string, DailyRankDetail> ParseDetails(string markdown)
    {
        var details = new Dictionary<string, DailyRankDetail>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DetailSectionRegex.Matches(markdown))
        {
            var url = match.Groups["url"].Value.Trim();
            var fullName = url.Replace("https://github.com/", "", StringComparison.OrdinalIgnoreCase)
                .Trim('/');
            if (fullName.Count(character => character == '/') != 1)
            {
                continue;
            }

            var body = match.Groups["body"].Value;
            var descriptionMatch = DescriptionRegex.Match(body);
            var createdMatch = CreatedRegex.Match(body);
            details[fullName] = new DailyRankDetail
            {
                Description = descriptionMatch.Success
                    ? descriptionMatch.Groups["description"].Value.Trim()
                    : "",
                CreatedAt = createdMatch.Success &&
                            DateTime.TryParseExact(
                                createdMatch.Groups["date"].Value,
                                "yyyy-MM-dd",
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal,
                                out var createdAt)
                    ? new DateTimeOffset(createdAt, TimeSpan.Zero)
                    : null
            };
        }

        return details;
    }

    private static int ParseInteger(string value) =>
        int.TryParse(
            value.Replace(",", "").Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var result)
            ? result
            : 0;

    private static int ParseCompactNumber(string value)
    {
        var normalized = value.Replace(",", "").Trim();
        if (normalized.EndsWith('k') || normalized.EndsWith('K'))
        {
            return double.TryParse(
                normalized[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var thousands)
                ? (int)Math.Round(thousands * 1_000)
                : 0;
        }

        if (normalized.EndsWith('m') || normalized.EndsWith('M'))
        {
            return double.TryParse(
                normalized[..^1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var millions)
                ? (int)Math.Round(millions * 1_000_000)
                : 0;
        }

        return ParseInteger(normalized);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary cache cleanup is best effort.
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class DailySnapshot
    {
        public required DateOnly Date { get; init; }
        public List<DailyRankRow> Rows { get; init; } = [];
    }

    private sealed class DailyRankRow
    {
        public required string FullName { get; init; }
        public required string HtmlUrl { get; init; }
        public int CurrentStars { get; init; }
        public int StarIncrease { get; init; }
        public string Description { get; init; } = "";
        public DateTimeOffset? CreatedAt { get; init; }
    }

    private sealed class DailyRankDetail
    {
        public string Description { get; init; } = "";
        public DateTimeOffset? CreatedAt { get; init; }
    }

    private sealed class AggregateEntry
    {
        public required string FullName { get; init; }
        public required string HtmlUrl { get; init; }
        public string Description { get; set; } = "";
        public int CurrentStars { get; set; }
        public int StarIncrease { get; set; }
        public int AppearanceDays { get; set; }
        public DateOnly FirstSeenDate { get; init; }
        public DateOnly LastSeenDate { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
    }
}
