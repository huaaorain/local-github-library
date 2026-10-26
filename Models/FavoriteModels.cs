namespace GitHubTopicReports.Models;

public sealed class FavoritesDocument
{
    public int Version { get; init; } = 1;
    public List<FavoriteFolder> Folders { get; init; } = [];
}

public sealed class FavoriteFolder
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    public List<FavoriteRepository> Repositories { get; init; } = [];

    public string CountDisplay => $"{Repositories.Count:N0} 个仓库";

    public bool ContainsRepository(long repositoryId, string fullName) =>
        Repositories.Any(item =>
            item.Id == repositoryId ||
            string.Equals(
                item.FullName,
                fullName,
                StringComparison.OrdinalIgnoreCase));

    public bool ContainsRepository(RepositoryRecord repository) =>
        ContainsRepository(repository.Id, repository.FullName);
}

public sealed class FavoriteRepository
{
    public long Id { get; init; }
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
    public string SourceKeyword { get; init; } = "";
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;

    public string StarsDisplay => Stars.ToString("N0");
    public string TopicsDisplay => Topics.Count == 0 ? "无" : string.Join(", ", Topics);
    public string SavedAtDisplay => SavedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed class FavoriteAddResult
{
    public bool Added { get; init; }
    public bool AlreadyExists { get; init; }
    public required string FolderName { get; init; }
    public required string Message { get; init; }
}

public sealed class FavoriteBatchAddResult
{
    public int AddedCount { get; init; }
    public int DuplicateCount { get; init; }
    public required string FolderName { get; init; }
}
