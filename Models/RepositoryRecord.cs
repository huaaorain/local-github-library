using System.Text.Json.Serialization;

namespace GitHubTopicReports.Models;

public sealed class RepositorySearchResponse
{
    [JsonPropertyName("total_count")]
    public int TotalCount { get; set; }

    [JsonPropertyName("items")]
    public List<GitHubRepositoryDto> Items { get; set; } = [];
}

public sealed class GitHubRepositoryDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("stargazers_count")]
    public int StargazersCount { get; set; }

    [JsonPropertyName("forks_count")]
    public int ForksCount { get; set; }

    [JsonPropertyName("open_issues_count")]
    public int OpenIssuesCount { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("topics")]
    public List<string> Topics { get; set; } = [];

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("pushed_at")]
    public DateTimeOffset? PushedAt { get; set; }

    [JsonPropertyName("license")]
    public GitHubLicenseDto? License { get; set; }
}

public sealed class GitHubLicenseDto
{
    [JsonPropertyName("spdx_id")]
    public string? SpdxId { get; set; }
}

public sealed class RepositoryRecord
{
    public long Id { get; init; }
    public int Rank { get; set; }
    public string FullName { get; init; } = "";
    public string HtmlUrl { get; init; } = "";
    public string Description { get; init; } = "";
    public int Stars { get; init; }
    public int Forks { get; init; }
    public int OpenIssues { get; init; }
    public string Language { get; init; } = "";
    public IReadOnlyList<string> Topics { get; init; } = [];
    public DateTimeOffset? UpdatedAt { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? PushedAt { get; init; }
    public string License { get; init; } = "";
    public int? WeeklyGrowth { get; set; }

    public string StarsDisplay => Stars.ToString("N0");
    public string GrowthDisplay => WeeklyGrowth is null
        ? "无基线"
        : WeeklyGrowth.Value.ToString("+#,##0;-#,##0;0");
    public string UpdatedDisplay => UpdatedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "未知";
    public string TopicsDisplay => Topics.Count == 0 ? "无" : string.Join(", ", Topics);
}

public sealed class HistoryEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Keyword { get; init; } = "";
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string DisplayTimestamp => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public override string ToString() => $"{DisplayTimestamp}  {Keyword}";
}

public sealed class StarSnapshotLine
{
    public DateTimeOffset Timestamp { get; init; }
    public string Keyword { get; init; } = "";
    public string ReportFile { get; init; } = "";
    public List<StarSnapshotRepository> Repositories { get; init; } = [];
}

public sealed class StarSnapshotRepository
{
    public long Id { get; init; }
    public string FullName { get; init; } = "";
    public int Stars { get; init; }
}

public sealed class GrowthResult
{
    public HistoryEntry? Baseline { get; init; }
    public List<RepositoryRecord> Repositories { get; init; } = [];
    public string Message { get; init; } = "";
}

public sealed class SavedReport
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
}
