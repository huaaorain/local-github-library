using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class ReportLibraryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public ReportLibraryService(string? libraryRoot = null)
    {
        LibraryRoot = string.IsNullOrWhiteSpace(libraryRoot)
            ? Path.Combine(AppContext.BaseDirectory, "GitHubReports")
            : Path.GetFullPath(libraryRoot);
        ReportsDirectory = Path.Combine(LibraryRoot, "Reports");
        DataDirectory = Path.Combine(LibraryRoot, "Data");
        IndexFilePath = Path.Combine(DataDirectory, "搜索记录.txt");
        SnapshotFilePath = Path.Combine(DataDirectory, "star-snapshots.jsonl");

        Directory.CreateDirectory(ReportsDirectory);
        Directory.CreateDirectory(DataDirectory);
    }

    public string LibraryRoot { get; }
    public string ReportsDirectory { get; }
    public string DataDirectory { get; }
    public string IndexFilePath { get; }
    public string SnapshotFilePath { get; }

    public async Task<IReadOnlyList<HistoryEntry>> LoadHistoryAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(IndexFilePath))
        {
            return [];
        }

        var entries = new List<HistoryEntry>();
        foreach (var line in await File.ReadAllLinesAsync(IndexFilePath, Utf8NoBom, cancellationToken))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3 ||
                !DateTimeOffset.TryParseExact(
                    parts[0],
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal,
                    out var timestamp))
            {
                continue;
            }

            var fileName = parts[^1];
            var keyword = string.Join(' ', parts.Skip(1).Take(parts.Length - 2));
            var filePath = Path.Combine(ReportsDirectory, fileName);
            if (!File.Exists(filePath))
            {
                continue;
            }

            entries.Add(new HistoryEntry
            {
                Timestamp = timestamp,
                Keyword = keyword,
                FileName = fileName,
                FilePath = filePath
            });
        }

        return entries
            .OrderByDescending(entry => entry.Timestamp)
            .ToList();
    }

    public async Task<GrowthResult> CalculateWeeklyGrowthAsync(
        string keyword,
        IReadOnlyList<RepositoryRecord> currentRepositories,
        DateTimeOffset generatedAt,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await LoadSnapshotsAsync(cancellationToken);
        var baselineDate = generatedAt.AddDays(-7);
        var normalizedKeyword = NormalizeKeyword(keyword);

        var baseline = snapshots
            .Where(snapshot =>
                snapshot.Timestamp <= baselineDate &&
                NormalizeKeyword(snapshot.Keyword) == normalizedKeyword)
            .OrderByDescending(snapshot => snapshot.Timestamp)
            .FirstOrDefault();

        if (baseline is null)
        {
            return new GrowthResult
            {
                Message = "当前关键词还没有至少 7 天前的本地报告。今天的记录已保存，下一次周查询即可生成真实增长榜。"
            };
        }

        var previousStars = baseline.Repositories
            .GroupBy(repository => repository.Id)
            .ToDictionary(group => group.Key, group => group.First().Stars);

        var growthRepositories = new List<RepositoryRecord>();
        foreach (var repository in currentRepositories)
        {
            if (!previousStars.TryGetValue(repository.Id, out var previous))
            {
                continue;
            }

            var growth = repository.Stars - previous;
            if (growth <= 0)
            {
                continue;
            }

            repository.WeeklyGrowth = growth;
            growthRepositories.Add(repository);
        }

        var ranked = growthRepositories
            .OrderByDescending(repository => repository.WeeklyGrowth)
            .ThenByDescending(repository => repository.Stars)
            .Take(Math.Clamp(maxResults, 1, 1000))
            .ToList();

        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].Rank = i + 1;
        }

        var baselineEntry = new HistoryEntry
        {
            Timestamp = baseline.Timestamp,
            Keyword = baseline.Keyword,
            FileName = baseline.ReportFile,
            FilePath = Path.Combine(ReportsDirectory, baseline.ReportFile)
        };

        return new GrowthResult
        {
            Baseline = baselineEntry,
            Repositories = ranked,
            Message = ranked.Count == 0
                ? $"已找到基线报告 {baseline.ReportFile}，但候选仓库没有可计算的 Star 增长。"
                : $"基线报告：{baseline.ReportFile}（{baselineEntry.DisplayTimestamp}）。"
        };
    }

    public async Task<SavedReport> SaveReportAsync(
        string originalKeyword,
        DateTimeOffset generatedAt,
        IReadOnlyList<RepositoryRecord> allRepositories,
        GrowthResult growth,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var timestamp = generatedAt.ToLocalTime();
            var baseName = timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var fileName = $"{baseName}.txt";
            var fullPath = Path.Combine(ReportsDirectory, fileName);
            var suffix = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"{baseName}_{suffix++}.txt";
                fullPath = Path.Combine(ReportsDirectory, fileName);
            }

            var currentTop = allRepositories.ToList();
            var reportText = BuildReport(
                originalKeyword,
                generatedAt,
                currentTop,
                growth,
                allRepositories);

            await File.WriteAllTextAsync(fullPath, reportText, Utf8NoBom, cancellationToken);

            var indexLine =
                $"{timestamp:yyyy-MM-dd HH:mm:ss}\t{SanitizeField(originalKeyword)}\t{fileName}" +
                Environment.NewLine;
            await File.AppendAllTextAsync(IndexFilePath, indexLine, Utf8NoBom, cancellationToken);

            var snapshot = new StarSnapshotLine
            {
                Timestamp = generatedAt,
                Keyword = originalKeyword,
                ReportFile = fileName,
                Repositories = allRepositories.Select(repository => new StarSnapshotRepository
                {
                    Id = repository.Id,
                    FullName = repository.FullName,
                    Stars = repository.Stars
                }).ToList()
            };
            var snapshotLine = JsonSerializer.Serialize(snapshot, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(
                SnapshotFilePath,
                snapshotLine,
                Utf8NoBom,
                cancellationToken);

            return new SavedReport
            {
                FilePath = fullPath,
                FileName = fileName,
                GeneratedAt = generatedAt
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<SavedReport> SaveGlobalLeaderboardReportAsync(
        GlobalLeaderboardResult leaderboard,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var generatedAt = DateTimeOffset.Now;
            var timestamp = generatedAt.ToLocalTime();
            var baseName = timestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            var fileName = $"{baseName}.txt";
            var fullPath = Path.Combine(ReportsDirectory, fileName);
            var suffix = 1;
            while (File.Exists(fullPath))
            {
                fileName = $"{baseName}_{suffix++}.txt";
                fullPath = Path.Combine(ReportsDirectory, fileName);
            }

            var isCurrent = leaderboard.Mode == GlobalLeaderboardMode.Current;
            var keyword = isCurrent ? "总榜-当前 Star" : $"总榜-{leaderboard.PeriodTitle}";
            var builder = new StringBuilder();
            builder.AppendLine(isCurrent
                ? "GitHub 全站 Star 现状总榜"
                : "GitHub 全站 Star 增量总榜");
            builder.AppendLine(new string('=', 72));
            if (isCurrent)
            {
                builder.AppendLine("榜单类型：当前 Star");
            }
            else
            {
                builder.AppendLine($"统计周期：{leaderboard.PeriodTitle}（{leaderboard.PeriodDays} 天）");
            }

            builder.AppendLine($"报告生成时间：{generatedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}");
            builder.AppendLine($"榜单数据截止：{leaderboard.LatestDataDate:yyyy-MM-dd}");
            if (!isCurrent)
            {
                builder.AppendLine($"统计起始日期：{leaderboard.StartDataDate:yyyy-MM-dd}");
                builder.AppendLine($"使用日榜文件数：{leaderboard.SourceFileCount}");
            }

            builder.AppendLine($"缓存状态：{leaderboard.CacheStatus}");
            builder.AppendLine($"数据说明：{leaderboard.SourceNote}");
            builder.AppendLine();
            builder.AppendLine($"排行记录：{leaderboard.Entries.Count:N0}");
            builder.AppendLine(new string('-', 72));

            foreach (var entry in leaderboard.Entries)
            {
                builder.AppendLine($"{entry.Rank,3}. {entry.FullName}");
                builder.AppendLine($"     当前 Star：{entry.CurrentStars:N0}");
                if (!isCurrent)
                {
                    builder.AppendLine($"     Star 增量：{entry.StarIncrease:+#,##0;-#,##0;0}");
                    builder.AppendLine($"     上榜天数：{entry.AppearanceDays:N0}");
                    builder.AppendLine($"     首次上榜：{entry.FirstSeenDate:yyyy-MM-dd}");
                    builder.AppendLine($"     最近上榜：{entry.LastSeenDate:yyyy-MM-dd}");
                }

                builder.AppendLine($"     开源时间：{FormatDate(entry.CreatedAt)}");
                builder.AppendLine($"     GitHub：{entry.HtmlUrl}");
                builder.AppendLine($"     项目描述：{(string.IsNullOrWhiteSpace(entry.Description) ? "无" : entry.Description)}");
                builder.AppendLine();
            }

            await File.WriteAllTextAsync(fullPath, builder.ToString(), Utf8NoBom, cancellationToken);
            var indexLine =
                $"{timestamp:yyyy-MM-dd HH:mm:ss}\t{SanitizeField(keyword)}\t{fileName}" +
                Environment.NewLine;
            await File.AppendAllTextAsync(IndexFilePath, indexLine, Utf8NoBom, cancellationToken);

            return new SavedReport
            {
                FilePath = fullPath,
                FileName = fileName,
                GeneratedAt = generatedAt
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<List<StarSnapshotLine>> LoadSnapshotsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SnapshotFilePath))
        {
            return [];
        }

        var snapshots = new List<StarSnapshotLine>();
        foreach (var line in await File.ReadAllLinesAsync(
                     SnapshotFilePath,
                     Utf8NoBom,
                     cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var snapshot = JsonSerializer.Deserialize<StarSnapshotLine>(line, JsonOptions);
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }
            catch (JsonException)
            {
                // A damaged historical line should not prevent new reports.
            }
        }

        return snapshots;
    }

    private static string BuildReport(
        string keyword,
        DateTimeOffset generatedAt,
        IReadOnlyList<RepositoryRecord> currentTop,
        GrowthResult growth,
        IReadOnlyList<RepositoryRecord> allRepositories)
    {
        var localTime = generatedAt.ToLocalTime();
        var builder = new StringBuilder();

        builder.AppendLine("GitHub 主题分析报告");
        builder.AppendLine(new string('=', 72));
        builder.AppendLine($"搜索关键词：{keyword}");
        builder.AppendLine($"报告生成时间：{localTime:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine("数据来源：GitHub REST API");
        builder.AppendLine($"本次采集记录数：{allRepositories.Count}");
        builder.AppendLine($"周增长基线：{(growth.Baseline is null ? "无" : growth.Baseline.FileName)}");
        builder.AppendLine($"说明：{growth.Message}");
        builder.AppendLine();

        AppendRankingSection(
            builder,
            $"一、当前 Star 排行（共 {currentTop.Count:N0} 条）",
            currentTop,
            includeGrowth: false);

        builder.AppendLine();
        AppendRankingSection(
            builder,
            $"二、本地报告中过去一周新增 Star 排行（共 {growth.Repositories.Count:N0} 条）",
            growth.Repositories,
            includeGrowth: true);

        builder.AppendLine();
        builder.AppendLine(new string('-', 72));
        builder.AppendLine($"三、本次获取到的全部 {allRepositories.Count} 条仓库记录与详解");
        builder.AppendLine(new string('-', 72));

        for (var i = 0; i < allRepositories.Count; i++)
        {
            AppendRepositoryDetail(builder, allRepositories[i], i + 1);
        }

        return builder.ToString();
    }

    private static void AppendRankingSection(
        StringBuilder builder,
        string title,
        IReadOnlyList<RepositoryRecord> repositories,
        bool includeGrowth)
    {
        builder.AppendLine(title);
        builder.AppendLine(new string('-', 72));

        if (repositories.Count == 0)
        {
            builder.AppendLine("暂无记录。");
            return;
        }

        var rank = 1;
        foreach (var repository in repositories)
        {
            builder.Append($"{rank++,2}. {repository.FullName}");
            builder.Append($"  Stars: {repository.Stars:N0}");
            if (includeGrowth && repository.WeeklyGrowth is not null)
            {
                builder.Append($"  周新增: {repository.WeeklyGrowth.Value:+#,##0;-#,##0;0}");
            }

            builder.AppendLine();
            builder.AppendLine($"    {repository.HtmlUrl}");
        }
    }

    private static void AppendRepositoryDetail(
        StringBuilder builder,
        RepositoryRecord repository,
        int index)
    {
        builder.AppendLine();
        builder.AppendLine($"{index}. {repository.FullName}");
        builder.AppendLine($"   GitHub：{repository.HtmlUrl}");
        builder.AppendLine($"   Star：{repository.Stars:N0}");
        builder.AppendLine($"   Fork：{repository.Forks:N0}");
        builder.AppendLine($"   Open Issues：{repository.OpenIssues:N0}");
        builder.AppendLine($"   Language：{repository.Language}");
        builder.AppendLine($"   License：{repository.License}");
        builder.AppendLine($"   Created：{FormatDate(repository.CreatedAt)}");
        builder.AppendLine($"   Updated：{FormatDate(repository.UpdatedAt)}");
        builder.AppendLine($"   Pushed：{FormatDate(repository.PushedAt)}");
        builder.AppendLine($"   Topics：{repository.TopicsDisplay}");
        builder.AppendLine($"   Description：{(string.IsNullOrWhiteSpace(repository.Description) ? "无" : repository.Description)}");
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";

    private static string SanitizeField(string value) =>
        value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string NormalizeKeyword(string value) =>
        SanitizeField(value).ToUpperInvariant();

    public void OpenLibrary()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = LibraryRoot,
            UseShellExecute = true
        });
    }
}
