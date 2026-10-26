namespace GitHubTopicReports.Models;

public enum GlobalLeaderboardPeriod
{
    Day,
    Week,
    Month,
    TwoMonths,
    HalfYear,
    Year
}

public enum GlobalLeaderboardMode
{
    Increase,
    Current
}

public sealed class GlobalLeaderboardEntry
{
    public int Rank { get; set; }
    public string FullName { get; init; } = "";
    public string HtmlUrl { get; init; } = "";
    public string Description { get; init; } = "";
    public int CurrentStars { get; init; }
    public int StarIncrease { get; init; }
    public int AppearanceDays { get; init; }
    public DateOnly FirstSeenDate { get; init; }
    public DateOnly LastSeenDate { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }

    public string IncreaseDisplay => StarIncrease.ToString("+#,##0;-#,##0;0");
    public string CurrentStarsDisplay => CurrentStars.ToString("N0");
    public string FirstSeenDisplay => FirstSeenDate.ToString("yyyy-MM-dd");
    public string LastSeenDisplay => LastSeenDate.ToString("yyyy-MM-dd");
}

public sealed class GlobalLeaderboardResult
{
    public required GlobalLeaderboardMode Mode { get; init; }
    public required string PeriodTitle { get; init; }
    public required int PeriodDays { get; init; }
    public required DateOnly LatestDataDate { get; init; }
    public required DateOnly StartDataDate { get; init; }
    public required int SourceFileCount { get; init; }
    public required string CacheStatus { get; init; }
    public required string SourceNote { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public List<GlobalLeaderboardEntry> Entries { get; init; } = [];
}

public sealed class GlobalLeaderboardModeOption
{
    public required string Name { get; init; }
    public required GlobalLeaderboardMode Mode { get; init; }

    public static IReadOnlyList<GlobalLeaderboardModeOption> All { get; } =
    [
        new() { Name = "Star 增量", Mode = GlobalLeaderboardMode.Increase },
        new() { Name = "当前 Star", Mode = GlobalLeaderboardMode.Current }
    ];
}

public sealed class GlobalLeaderboardPeriodOption
{
    public required string Name { get; init; }
    public required GlobalLeaderboardPeriod Period { get; init; }
    public required int Days { get; init; }

    public static IReadOnlyList<GlobalLeaderboardPeriodOption> All { get; } =
    [
        new() { Name = "日榜 · 1 天", Period = GlobalLeaderboardPeriod.Day, Days = 1 },
        new() { Name = "周榜 · 7 天", Period = GlobalLeaderboardPeriod.Week, Days = 7 },
        new() { Name = "月榜 · 30 天", Period = GlobalLeaderboardPeriod.Month, Days = 30 },
        new() { Name = "双月榜 · 60 天", Period = GlobalLeaderboardPeriod.TwoMonths, Days = 60 },
        new() { Name = "半年榜 · 182 天", Period = GlobalLeaderboardPeriod.HalfYear, Days = 182 },
        new() { Name = "年榜 · 365 天", Period = GlobalLeaderboardPeriod.Year, Days = 365 }
    ];
}
