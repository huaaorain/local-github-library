using System.IO;
using System.Text;
using System.Text.Json;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class FavoritesService
{
    private const string DefaultFolderName = "默认收藏";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FavoritesService(string libraryRoot)
    {
        DataDirectory = Path.Combine(Path.GetFullPath(libraryRoot), "Data");
        FavoritesFilePath = Path.Combine(DataDirectory, "favorites.json");
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }
    public string FavoritesFilePath { get; }

    public async Task<IReadOnlyList<FavoriteFolder>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var document = await LoadDocumentInternalAsync(cancellationToken);
        return document.Folders
            .OrderBy(folder => folder.CreatedAt)
            .ToList();
    }

    public async Task<FavoriteFolder> CreateFolderAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentInternalAsync(cancellationToken);
            var normalizedName = ValidateFolderName(name, document, null);
            var folder = new FavoriteFolder
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                CreatedAt = DateTimeOffset.Now
            };

            document.Folders.Add(folder);
            await SaveDocumentInternalAsync(document, cancellationToken);
            return folder;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RenameFolderAsync(
        string folderId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentInternalAsync(cancellationToken);
            var folder = document.Folders.FirstOrDefault(item => item.Id == folderId)
                ?? throw new InvalidOperationException("所选收藏目录不存在。");
            folder.Name = ValidateFolderName(newName, document, folderId);
            await SaveDocumentInternalAsync(document, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<FavoriteAddResult> AddFavoriteAsync(
        string folderId,
        string sourceKeyword,
        RepositoryRecord repository,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentInternalAsync(cancellationToken);
            var folder = document.Folders.FirstOrDefault(item => item.Id == folderId)
                ?? throw new InvalidOperationException("请选择一个收藏目录。");

            if (folder.ContainsRepository(repository))
            {
                return new FavoriteAddResult
                {
                    Added = false,
                    AlreadyExists = true,
                    FolderName = folder.Name,
                    Message = $"“{repository.FullName}”已经在“{folder.Name}”中。"
                };
            }

            folder.Repositories.Add(new FavoriteRepository
            {
                Id = repository.Id,
                FullName = repository.FullName,
                HtmlUrl = repository.HtmlUrl,
                Description = repository.Description,
                Stars = repository.Stars,
                Forks = repository.Forks,
                OpenIssues = repository.OpenIssues,
                Language = repository.Language,
                Topics = repository.Topics.ToList(),
                UpdatedAt = repository.UpdatedAt,
                CreatedAt = repository.CreatedAt,
                PushedAt = repository.PushedAt,
                License = repository.License,
                SourceKeyword = sourceKeyword,
                SavedAt = DateTimeOffset.Now
            });

            await SaveDocumentInternalAsync(document, cancellationToken);
            return new FavoriteAddResult
            {
                Added = true,
                AlreadyExists = false,
                FolderName = folder.Name,
                Message = $"已将“{repository.FullName}”收藏到“{folder.Name}”。"
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> RemoveFavoriteAsync(
        string folderId,
        long repositoryId,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentInternalAsync(cancellationToken);
            var folder = document.Folders.FirstOrDefault(item => item.Id == folderId)
                ?? throw new InvalidOperationException("所选收藏目录不存在。");
            var removed = folder.Repositories.RemoveAll(item => item.Id == repositoryId) > 0;
            if (removed)
            {
                await SaveDocumentInternalAsync(document, cancellationToken);
            }

            return removed;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<FavoriteBatchAddResult> AddFavoritesAsync(
        string folderId,
        string sourceKeyword,
        IReadOnlyList<RepositoryRecord> repositories,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadDocumentInternalAsync(cancellationToken);
            var folder = document.Folders.FirstOrDefault(item => item.Id == folderId)
                ?? throw new InvalidOperationException("请选择一个收藏目录。");
            var added = 0;
            var duplicates = 0;

            foreach (var repository in repositories)
            {
                var exists = folder.ContainsRepository(repository);
                if (exists)
                {
                    duplicates++;
                    continue;
                }

                folder.Repositories.Add(new FavoriteRepository
                {
                    Id = repository.Id,
                    FullName = repository.FullName,
                    HtmlUrl = repository.HtmlUrl,
                    Description = repository.Description,
                    Stars = repository.Stars,
                    Forks = repository.Forks,
                    OpenIssues = repository.OpenIssues,
                    Language = repository.Language,
                    Topics = repository.Topics.ToList(),
                    UpdatedAt = repository.UpdatedAt,
                    CreatedAt = repository.CreatedAt,
                    PushedAt = repository.PushedAt,
                    License = repository.License,
                    SourceKeyword = sourceKeyword,
                    SavedAt = DateTimeOffset.Now
                });
                added++;
            }

            if (added > 0)
            {
                await SaveDocumentInternalAsync(document, cancellationToken);
            }

            return new FavoriteBatchAddResult
            {
                AddedCount = added,
                DuplicateCount = duplicates,
                FolderName = folder.Name
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<FavoritesDocument> LoadDocumentInternalAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(FavoritesFilePath))
        {
            var initial = new FavoritesDocument
            {
                Folders =
                [
                    new FavoriteFolder
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = DefaultFolderName,
                        CreatedAt = DateTimeOffset.Now
                    }
                ]
            };
            await SaveDocumentInternalAsync(initial, cancellationToken);
            return initial;
        }

        try
        {
            await using var stream = File.OpenRead(FavoritesFilePath);
            var document = await JsonSerializer.DeserializeAsync<FavoritesDocument>(
                stream,
                JsonOptions,
                cancellationToken);
            return document ?? throw new InvalidDataException("收藏文件内容为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"收藏文件格式损坏：{FavoritesFilePath}",
                exception);
        }
    }

    private async Task SaveDocumentInternalAsync(
        FavoritesDocument document,
        CancellationToken cancellationToken)
    {
        var temporaryPath = FavoritesFilePath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(document, JsonOptions),
            Utf8NoBom,
            cancellationToken);
        File.Move(temporaryPath, FavoritesFilePath, overwrite: true);
    }

    private static string ValidateFolderName(
        string value,
        FavoritesDocument document,
        string? exceptFolderId)
    {
        var name = value.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("目录名称不能为空。");
        }

        if (name.Length > 40)
        {
            throw new InvalidOperationException("目录名称不能超过 40 个字符。");
        }

        if (name.Contains('\r') || name.Contains('\n') || name.Contains('\t'))
        {
            throw new InvalidOperationException("目录名称不能包含换行或制表符。");
        }

        if (document.Folders.Any(folder =>
                folder.Id != exceptFolderId &&
                string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DuplicateFavoriteFolderException();
        }

        return name;
    }
}

public sealed class DuplicateFavoriteFolderException : Exception
{
    public DuplicateFavoriteFolderException()
        : base("已存在同名目录，请勿重复建目录。")
    {
    }
}
