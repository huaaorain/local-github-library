using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed record DecryptedNoteResult(NoteContent Content, NoteCryptoKey Key);

public sealed class NotesService
{
    private const int MaxTitleLength = 200;
    private const int MaxContentLength = 2_000_000;
    private const string ImagesDirectoryName = "images";
    private const string RichTextMarkerPrefix = "<!-- ghll-rich-text-data:";
    private const string RichTextMarkerSuffix = " -->";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
    private static readonly Regex DataImageRegex = new(
        @"!\[[^\]]*\]\((?<url>data:image/[^)\s]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _locationLock = new();
    private readonly Dictionary<string, NoteIndexEntry> _locations =
        new(StringComparer.Ordinal);

    public NotesService(string libraryRoot)
    {
        NotesDirectory = Path.Combine(Path.GetFullPath(libraryRoot), "Notes");
        IndexFilePath = Path.Combine(NotesDirectory, "notes-index.json");
        Directory.CreateDirectory(NotesDirectory);
    }

    public string NotesDirectory { get; }
    public string IndexFilePath { get; }

    public string CreateAttachmentFilePath(string noteId, string extension)
    {
        ValidateId(noteId);
        var normalizedExtension = extension.StartsWith('.')
            ? extension
            : "." + extension;
        var noteFolder = TryGetCachedNoteFolder(noteId);
        if (noteFolder is not null)
        {
            var directory = Path.Combine(noteFolder, ImagesDirectoryName);
            Directory.CreateDirectory(directory);
            return Path.Combine(
                directory,
                $"image-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{normalizedExtension}");
        }

        var legacyDirectory = Path.Combine(NotesDirectory, "Attachments", noteId);
        Directory.CreateDirectory(legacyDirectory);
        return Path.Combine(
            legacyDirectory,
            $"image-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{normalizedExtension}");
    }

    public async Task<string> SaveEncryptedAttachmentAsync(
        string noteId,
        string extension,
        byte[] bytes,
        NoteCryptoKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateId(noteId);
        var noteFolder = TryGetCachedNoteFolder(noteId)
                         ?? throw new InvalidOperationException("笔记目录尚未创建。");
        var imagesDirectory = Path.Combine(noteFolder, ImagesDirectoryName);
        Directory.CreateDirectory(imagesDirectory);
        var normalizedExtension = extension.StartsWith('.')
            ? extension
            : "." + extension;
        var fileName =
            $"image-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{normalizedExtension}.enc";
        var path = Path.Combine(imagesDirectory, fileName);
        var payload = NoteEncryptionService.Encrypt(
            new NoteContent("", Convert.ToBase64String(bytes)),
            key);
        await WriteTextAtomicallyAsync(
            path,
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken,
            ValidateJsonDocument);
        return $"{ImagesDirectoryName}/{fileName}";
    }

    public async Task<string> SaveAttachmentAsync(
        string noteId,
        string extension,
        byte[] bytes,
        CancellationToken cancellationToken = default)
    {
        var path = CreateAttachmentFilePath(noteId, extension);
        await WriteBytesAtomicallyAsync(path, bytes, cancellationToken);
        return GetAttachmentReference(noteId, path);
    }

    public string GetAttachmentReference(string noteId, string filePath)
    {
        ValidateId(noteId);
        if (TryGetCachedNoteFolder(noteId) is { } noteFolder)
        {
            var target = Path.Combine(noteFolder, ImagesDirectoryName)
                         + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(filePath);
            if (fullPath.StartsWith(
                    Path.GetFullPath(target),
                    StringComparison.OrdinalIgnoreCase))
            {
                return $"{ImagesDirectoryName}/{Path.GetFileName(fullPath)}";
            }
        }

        return Path.GetRelativePath(
                NotesDirectory,
                filePath)
            .Replace('\\', '/');
    }

    public async Task<IReadOnlyList<NoteDocument>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            CacheLocations(index);
            return index.Notes
                .Where(entry => IsValidId(entry.Id))
                .OrderByDescending(entry => entry.UpdatedAt)
                .Select(entry => CreateDocument(entry))
                .ToList();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<NoteDocument> LoadAsync(
        string noteId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(noteId);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            CacheLocations(index);
            var entry = FindEntry(index, noteId)
                        ?? throw new FileNotFoundException("笔记不存在。");
            var note = CreateDocument(entry);
            if (entry.IsEncrypted)
            {
                return note;
            }

            var markdownPath = GetMarkdownPath(entry);
            if (!File.Exists(markdownPath))
            {
                throw new FileNotFoundException("笔记正文文件不存在。");
            }

            var parsed = ParseMarkdownFile(
                await File.ReadAllTextAsync(
                    markdownPath,
                    cancellationToken));
            note.Content = parsed.Content;
            note.RichTextData = parsed.RichTextData;
            return note;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<NoteFolder>> LoadFoldersAsync(
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            return index.Folders.ToList();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<NoteFolder> CreateFolderAsync(
        string name,
        string? parentId,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = name.Trim();
        if (normalizedName.Length is < 1 or > 60)
        {
            throw new InvalidOperationException("笔记目录名称需要为 1 到 60 个字符。");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            if (parentId is not null &&
                index.Folders.All(folder => folder.Id != parentId))
            {
                throw new InvalidOperationException("上级笔记目录不存在。");
            }

            if (index.Folders.Any(folder =>
                    folder.ParentId == parentId &&
                    string.Equals(folder.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("已存在同名笔记目录。");
            }

            var folder = new NoteFolder
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = normalizedName,
                ParentId = parentId,
                CreatedAt = DateTimeOffset.Now
            };
            index.Folders.Add(folder);
            var folderPaths = BuildFolderRelativePaths(index);
            if (folderPaths.TryGetValue(folder.Id, out var folderPath))
            {
                Directory.CreateDirectory(
                    Path.Combine(NotesDirectory, folderPath));
            }
            await SaveIndexInternalAsync(index, cancellationToken);
            return folder;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<NoteDocument> CreateAsync(
        string? folderId = null,
        int sortOrder = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            var now = DateTimeOffset.Now;
            var note = new NoteDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = now,
                UpdatedAt = now,
                Title = "未命名笔记",
                Content = "",
                FolderId = folderId,
                SortOrder = sortOrder
            };
            var entry = CreateEntry(index, note);
            note.RelativeFolderPath = entry.RelativePath;
            var noteFolder = GetNoteFolder(entry);
            Directory.CreateDirectory(Path.Combine(noteFolder, ImagesDirectoryName));
            await WriteTextAtomicallyAsync(
                GetMarkdownPath(entry),
                ComposeMarkdownFile(note.Content, note.RichTextData),
                cancellationToken);
            index.Notes.Add(entry);
            await SaveIndexInternalAsync(index, cancellationToken);
            CacheLocations(index);
            return note;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveAsync(
        NoteDocument note,
        CancellationToken cancellationToken = default)
    {
        ValidateNote(note);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            var entry = FindEntry(index, note.Id);
            if (entry is null)
            {
                entry = CreateEntry(index, note);
                index.Notes.Add(entry);
            }

            var targetRelativePath = AllocateNoteRelativePath(
                index,
                note.FolderId,
                note.Title,
                note.Id);
            MoveNoteDirectoryIfNeeded(entry, targetRelativePath);
            entry.Title = NormalizeTitle(note.Title);
            entry.FolderId = note.FolderId;
            entry.SortOrder = note.SortOrder;
            entry.UpdatedAt = DateTimeOffset.Now;
            entry.IsEncrypted = false;
            entry.RelativePath = targetRelativePath;

            var noteFolder = GetNoteFolder(entry);
            Directory.CreateDirectory(Path.Combine(noteFolder, ImagesDirectoryName));
            RewriteLegacyAttachmentReferences(note);
            CopyLegacyAttachments(note, noteFolder);
            await WriteTextAtomicallyAsync(
                GetMarkdownPath(entry),
                ComposeMarkdownFile(note.Content, note.RichTextData),
                cancellationToken);
            var encryptedPath = GetMarkdownPath(entry) + ".enc";
            if (File.Exists(encryptedPath))
            {
                File.Delete(encryptedPath);
            }

            DeleteLegacyNoteFile(note);
            TryDeleteDirectory(Path.Combine(
                NotesDirectory,
                "Attachments",
                note.Id));
            note.RelativeFolderPath = targetRelativePath;
            await SaveIndexInternalAsync(index, cancellationToken);
            CacheLocations(index);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveEncryptedAsync(
        NoteDocument note,
        NoteContent content,
        NoteCryptoKey key,
        CancellationToken cancellationToken = default)
    {
        ValidateNote(note);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            var entry = FindEntry(index, note.Id);
            if (entry is null)
            {
                entry = CreateEntry(index, note);
                index.Notes.Add(entry);
            }

            var targetRelativePath = AllocateNoteRelativePath(
                index,
                note.FolderId,
                content.Title,
                note.Id);
            MoveNoteDirectoryIfNeeded(entry, targetRelativePath);
            entry.Title = NormalizeTitle(content.Title);
            entry.FolderId = note.FolderId;
            entry.SortOrder = note.SortOrder;
            entry.UpdatedAt = DateTimeOffset.Now;
            entry.IsEncrypted = true;
            entry.RelativePath = targetRelativePath;

            var noteFolder = GetNoteFolder(entry);
            var imagesDirectory = Path.Combine(noteFolder, ImagesDirectoryName);
            Directory.CreateDirectory(imagesDirectory);
            var encryptedContent = ExternalizeEncryptedImages(
                content.Content,
                content.RichTextData,
                imagesDirectory,
                key);
            var markdown = ComposeMarkdownFile(
                encryptedContent.Content,
                encryptedContent.RichTextData);
            var payload = NoteEncryptionService.Encrypt(
                new NoteContent(content.Title, markdown),
                key);
            await WriteTextAtomicallyAsync(
                GetMarkdownPath(entry) + ".enc",
                JsonSerializer.Serialize(payload, JsonOptions),
                cancellationToken,
                ValidateJsonDocument);
            var plaintextPath = GetMarkdownPath(entry);
            if (File.Exists(plaintextPath))
            {
                File.Delete(plaintextPath);
            }

            note.Title = entry.Title;
            note.Content = encryptedContent.Content;
            note.RichTextData = encryptedContent.RichTextData;
            note.RelativeFolderPath = targetRelativePath;
            DeleteLegacyNoteFile(note);
            await SaveIndexInternalAsync(index, cancellationToken);
            CacheLocations(index);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<DecryptedNoteResult> OpenEncryptedAsync(
        string noteId,
        string password,
        CancellationToken cancellationToken = default)
    {
        ValidateId(noteId);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            var entry = FindEntry(index, noteId)
                        ?? throw new FileNotFoundException("笔记不存在。");
            var encryptedPath = GetMarkdownPath(entry) + ".enc";
            if (!File.Exists(encryptedPath))
            {
                throw new FileNotFoundException("加密笔记正文不存在。");
            }

            var payload = JsonSerializer.Deserialize<EncryptedNotePayload>(
                              await File.ReadAllTextAsync(
                                  encryptedPath,
                                  cancellationToken),
                              JsonOptions)
                          ?? throw new InvalidDataException("加密笔记数据为空。");
            NoteCryptoKey? key = NoteEncryptionService.RestoreKey(password, payload);
            try
            {
                var decrypted = NoteEncryptionService.Decrypt(payload, key);
                if (decrypted.RichTextData is null)
                {
                    var parsed = ParseMarkdownFile(decrypted.Content);
                    decrypted = parsed with { Title = decrypted.Title };
                }
                return new DecryptedNoteResult(decrypted, key);
            }
            catch
            {
                key.Dispose();
                throw;
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SaveNoteOrderAsync(
        IReadOnlyList<NoteDocument> notes,
        CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            foreach (var note in notes)
            {
                ValidateId(note.Id);
                var entry = FindEntry(index, note.Id);
                if (entry is null)
                {
                    continue;
                }

                var targetRelativePath = AllocateNoteRelativePath(
                    index,
                    note.FolderId,
                    entry.Title,
                    note.Id);
                MoveNoteDirectoryIfNeeded(entry, targetRelativePath);
                entry.FolderId = note.FolderId;
                entry.SortOrder = note.SortOrder;
                entry.RelativePath = targetRelativePath;
                entry.UpdatedAt = DateTimeOffset.Now;
            }
            await SaveIndexInternalAsync(index, cancellationToken);
            CacheLocations(index);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteAsync(
        string noteId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(noteId);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            var entry = FindEntry(index, noteId);
            if (entry is not null)
            {
                var folder = GetNoteFolder(entry);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
                index.Notes.Remove(entry);
            }

            var legacyPath = Path.Combine(NotesDirectory, $"{noteId}.note.json");
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
            var legacyAttachments = Path.Combine(
                NotesDirectory,
                "Attachments",
                noteId);
            if (Directory.Exists(legacyAttachments))
            {
                TryDeleteDirectory(legacyAttachments);
            }
            await SaveIndexInternalAsync(index, cancellationToken);
            CacheLocations(index);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteFolderAsync(
        string folderId,
        CancellationToken cancellationToken = default)
    {
        ValidateId(folderId);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var index = await LoadIndexInternalAsync(cancellationToken);
            await EnsureLegacyMigrationAsync(index, cancellationToken);
            if (index.Folders.All(folder => folder.Id != folderId))
            {
                throw new InvalidOperationException("要删除的笔记目录不存在。");
            }

            var folderIds = new HashSet<string>(StringComparer.Ordinal) { folderId };
            var addedFolder = true;
            while (addedFolder)
            {
                addedFolder = false;
                foreach (var folder in index.Folders)
                {
                    if (folder.ParentId is not null &&
                        folderIds.Contains(folder.ParentId) &&
                        folderIds.Add(folder.Id))
                    {
                        addedFolder = true;
                    }
                }
            }

            var folderPaths = BuildFolderRelativePaths(index);
            index.Folders.RemoveAll(folder => folderIds.Contains(folder.Id));
            foreach (var relativePath in folderIds
                         .Where(folderPaths.ContainsKey)
                         .Select(folderId => folderPaths[folderId])
                         .OrderByDescending(path => path.Length))
            {
                var folderPath = Path.Combine(NotesDirectory, relativePath);
                if (Directory.Exists(folderPath) &&
                    !Directory.EnumerateFiles(
                        folderPath,
                        "*",
                        SearchOption.AllDirectories).Any())
                {
                    TryDeleteDirectory(folderPath);
                }
            }
            await SaveIndexInternalAsync(index, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public string GetNotePath(string noteId)
    {
        ValidateId(noteId);
        if (TryGetCachedEntry(noteId) is { } entry)
        {
            var markdownPath = GetMarkdownPath(entry);
            return entry.IsEncrypted
                ? markdownPath + ".enc"
                : markdownPath;
        }

        var legacyPath = Path.Combine(NotesDirectory, $"{noteId}.note.json");
        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        throw new FileNotFoundException("笔记文件不存在。");
    }

    public byte[]? ReadAttachmentBytes(
        string noteId,
        string reference,
        NoteCryptoKey? key = null)
    {
        ValidateId(noteId);
        var relativeReference = reference
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var entry = TryGetCachedEntry(noteId);
        var baseDirectory = entry is not null &&
                            relativeReference
                                .Replace('\\', '/')
                                .StartsWith("images/", StringComparison.OrdinalIgnoreCase)
            ? GetNoteFolder(entry)
            : NotesDirectory;
        var path = Path.GetFullPath(Path.Combine(baseDirectory, relativeReference));
        var baseRoot = Path.GetFullPath(baseDirectory)
                       + Path.DirectorySeparatorChar;
        if (!path.StartsWith(baseRoot, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(path))
        {
            return null;
        }

        if (!path.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
        {
            return File.ReadAllBytes(path);
        }
        if (key is null)
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<EncryptedNotePayload>(
                File.ReadAllText(path),
                JsonOptions);
            if (payload is null)
            {
                return null;
            }
            var decrypted = NoteEncryptionService.Decrypt(payload, key);
            return Convert.FromBase64String(decrypted.Content);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or
            FormatException or NotePasswordException)
        {
            AppDiagnostics.LogException("读取加密笔记图片", exception);
            return null;
        }
    }

    private async Task EnsureLegacyMigrationAsync(
        NotesIndex index,
        CancellationToken cancellationToken)
    {
        var originalVersion = index.Version;
        var legacyFiles = Directory
            .EnumerateFiles(NotesDirectory, "*.note.json", SearchOption.TopDirectoryOnly)
            .ToList();
        if (legacyFiles.Count == 0)
        {
            if (index.Version < 2)
            {
                index.Version = 2;
                await SaveIndexInternalAsync(index, cancellationToken);
            }
            return;
        }

        var migrated = false;
        foreach (var legacyPath in legacyFiles)
        {
            try
            {
                NoteDocument? legacy;
                await using (var stream = File.OpenRead(legacyPath))
                {
                    legacy = await JsonSerializer.DeserializeAsync<NoteDocument>(
                        stream,
                        JsonOptions,
                        cancellationToken);
                }
                if (legacy is null || !IsValidId(legacy.Id))
                {
                    continue;
                }

                if (FindEntry(index, legacy.Id) is not null)
                {
                    continue;
                }

                if (legacy.Encrypted is null)
                {
                    await MigratePlainLegacyNoteAsync(index, legacy, cancellationToken);
                }
                else
                {
                    await MigrateEncryptedLegacyNoteAsync(index, legacy, cancellationToken);
                }
                File.Delete(legacyPath);
                migrated = true;
            }
            catch (Exception exception)
            {
                AppDiagnostics.LogException(
                    $"迁移旧版笔记 {Path.GetFileName(legacyPath)}",
                    exception);
            }
        }

        index.Version = 2;
        if (migrated || originalVersion < 2)
        {
            await SaveIndexInternalAsync(index, cancellationToken);
        }
    }

    private async Task MigratePlainLegacyNoteAsync(
        NotesIndex index,
        NoteDocument legacy,
        CancellationToken cancellationToken)
    {
        var targetRelativePath = AllocateNoteRelativePath(
            index,
            legacy.FolderId,
            legacy.Title,
            legacy.Id);
        var entry = CreateEntry(index, legacy);
        entry.RelativePath = targetRelativePath;
        entry.IsEncrypted = false;
        index.Notes.Add(entry);
        var noteFolder = GetNoteFolder(entry);
        Directory.CreateDirectory(Path.Combine(noteFolder, ImagesDirectoryName));
        CopyLegacyAttachments(legacy, noteFolder);
        RewriteLegacyAttachmentReferences(legacy);
        await WriteTextAtomicallyAsync(
            GetMarkdownPath(entry),
            ComposeMarkdownFile(legacy.Content, legacy.RichTextData),
            cancellationToken);
        TryDeleteDirectory(Path.Combine(NotesDirectory, "Attachments", legacy.Id));
    }

    private async Task MigrateEncryptedLegacyNoteAsync(
        NotesIndex index,
        NoteDocument legacy,
        CancellationToken cancellationToken)
    {
        var placeholderTitle =
            $"加密笔记-{legacy.Id[..Math.Min(6, legacy.Id.Length)]}";
        var targetRelativePath = AllocateNoteRelativePath(
            index,
            legacy.FolderId,
            placeholderTitle,
            legacy.Id);
        var entry = CreateEntry(index, legacy);
        entry.Title = placeholderTitle;
        entry.RelativePath = targetRelativePath;
        entry.IsEncrypted = true;
        index.Notes.Add(entry);
        var noteFolder = GetNoteFolder(entry);
        Directory.CreateDirectory(Path.Combine(noteFolder, ImagesDirectoryName));
        await WriteTextAtomicallyAsync(
            GetMarkdownPath(entry) + ".enc",
            JsonSerializer.Serialize(legacy.Encrypted, JsonOptions),
            cancellationToken,
            ValidateJsonDocument);
    }

    private async Task<NotesIndex> LoadIndexInternalAsync(
        CancellationToken cancellationToken)
    {
        foreach (var candidatePath in new[]
                 {
                     IndexFilePath,
                     IndexFilePath + ".bak"
                 })
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            try
            {
                var bytes = await File.ReadAllBytesAsync(
                    candidatePath,
                    cancellationToken);
                if (bytes.Length == 0 || bytes.All(value => value == 0))
                {
                    continue;
                }

                var index = JsonSerializer.Deserialize<NotesIndex>(
                                bytes,
                                JsonOptions)
                            ?? throw new InvalidDataException("笔记索引为空。");
                index.Folders ??= [];
                index.Notes ??= [];
                return index;
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or
                InvalidDataException)
            {
                AppDiagnostics.LogException(
                    $"读取笔记索引 {Path.GetFileName(candidatePath)}",
                    exception);
            }
        }

        if (File.Exists(IndexFilePath) ||
            File.Exists(IndexFilePath + ".bak"))
        {
            throw new InvalidDataException(
                "笔记索引和备份均已损坏。请保留 Notes 目录并联系维护者恢复。");
        }

        return new NotesIndex();
    }

    private async Task SaveIndexInternalAsync(
        NotesIndex index,
        CancellationToken cancellationToken)
    {
        index.Version = 2;
        await WriteTextAtomicallyAsync(
            IndexFilePath,
            JsonSerializer.Serialize(index, JsonOptions),
            cancellationToken,
            ValidateIndexDocument);
        CacheLocations(index);
    }

    private NoteDocument CreateDocument(NoteIndexEntry entry) =>
        new()
        {
            FormatVersion = 2,
            Id = entry.Id,
            CreatedAt = entry.CreatedAt,
            UpdatedAt = entry.UpdatedAt,
            Title = entry.Title,
            FolderId = entry.FolderId,
            SortOrder = entry.SortOrder,
            RelativeFolderPath = entry.RelativePath,
            Encrypted = entry.IsEncrypted
                ? new EncryptedNotePayload { Version = 1 }
                : null
        };

    private NoteIndexEntry CreateEntry(NotesIndex index, NoteDocument note)
    {
        var title = NormalizeTitle(note.Title);
        return new NoteIndexEntry
        {
            Id = note.Id,
            Title = title,
            RelativePath = AllocateNoteRelativePath(
                index,
                note.FolderId,
                title,
                note.Id),
            FolderId = note.FolderId,
            SortOrder = note.SortOrder,
            CreatedAt = note.CreatedAt,
            UpdatedAt = note.UpdatedAt,
            IsEncrypted = false
        };
    }

    private string AllocateNoteRelativePath(
        NotesIndex index,
        string? folderId,
        string title,
        string noteId)
    {
        var folderPaths = BuildFolderRelativePaths(index);
        var parentPath = folderId is not null &&
                         folderPaths.TryGetValue(folderId, out var mappedFolder)
            ? mappedFolder
            : "";
        var usedPaths = new HashSet<string>(
            index.Notes
                .Where(entry => entry.Id != noteId)
                .Select(entry => entry.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in folderPaths.Values)
        {
            usedPaths.Add(path);
        }

        var baseName = SanitizePathSegment(title);
        for (var suffix = 1; suffix < 10_000; suffix++)
        {
            var name = suffix == 1 ? baseName : $"{baseName} ({suffix})";
            var candidate = string.IsNullOrWhiteSpace(parentPath)
                ? name
                : Path.Combine(parentPath, name);
            if (!usedPaths.Contains(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(parentPath, $"{baseName} ({noteId[..8]})");
    }

    private static Dictionary<string, string> BuildFolderRelativePaths(
        NotesIndex index)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void AddChildren(string? parentId, string parentPath)
        {
            var siblings = index.Folders
                .Where(folder => folder.ParentId == parentId)
                .OrderBy(folder => folder.CreatedAt)
                .ToList();
            var usedNames = new HashSet<string>(
                index.Notes
                    .Where(entry => entry.FolderId == parentId)
                    .Select(entry => Path.GetFileName(entry.RelativePath)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var folder in siblings)
            {
                if (!visited.Add(folder.Id))
                {
                    continue;
                }

                var baseName = SanitizePathSegment(folder.Name);
                var name = baseName;
                for (var suffix = 2; !usedNames.Add(name); suffix++)
                {
                    name = $"{baseName} ({suffix})";
                }
                var relativePath = string.IsNullOrWhiteSpace(parentPath)
                    ? name
                    : Path.Combine(parentPath, name);
                result[folder.Id] = relativePath;
                AddChildren(folder.Id, relativePath);
            }
        }

        AddChildren(null, "");
        return result;
    }

    private void MoveNoteDirectoryIfNeeded(
        NoteIndexEntry entry,
        string targetRelativePath)
    {
        if (string.Equals(
                entry.RelativePath,
                targetRelativePath,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var currentFolder = GetNoteFolder(entry);
        var targetFolder = Path.Combine(NotesDirectory, targetRelativePath);
        Directory.CreateDirectory(
            Directory.GetParent(targetFolder)?.FullName ?? NotesDirectory);
        if (Directory.Exists(currentFolder))
        {
            Directory.Move(currentFolder, targetFolder);
        }
        entry.RelativePath = targetRelativePath;
    }

    private string GetNoteFolder(NoteIndexEntry entry) =>
        Path.Combine(
            NotesDirectory,
            entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));

    private string GetMarkdownPath(NoteIndexEntry entry)
    {
        var noteFolder = GetNoteFolder(entry);
        var fileName = Path.GetFileName(noteFolder) + ".md";
        return Path.Combine(noteFolder, fileName);
    }

    private void CopyLegacyAttachments(NoteDocument note)
    {
        if (TryGetCachedEntry(note.Id) is not { } entry)
        {
            return;
        }
        CopyLegacyAttachments(note, GetNoteFolder(entry));
    }

    private void CopyLegacyAttachments(
        NoteDocument note,
        string noteFolder)
    {
        var source = Path.Combine(NotesDirectory, "Attachments", note.Id);
        if (!Directory.Exists(source))
        {
            return;
        }

        var target = Path.Combine(noteFolder, ImagesDirectoryName);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var destination = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destination, overwrite: true);
        }
    }

    private void RewriteLegacyAttachmentReferences(NoteDocument note)
    {
        var legacyPrefix = $"Attachments/{note.Id}/";
        note.Content = note.Content.Replace(
            legacyPrefix,
            "images/",
            StringComparison.OrdinalIgnoreCase);
        if (note.RichTextData is not null)
        {
            note.RichTextData = note.RichTextData.Replace(
                legacyPrefix,
                "images/",
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private NoteContent ExternalizeEncryptedImages(
        string content,
        string? richTextData,
        string imagesDirectory,
        NoteCryptoKey key)
    {
        foreach (var file in Directory.EnumerateFiles(imagesDirectory))
        {
            if (file.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(file);
            var encryptedPath = file + ".enc";
            var payload = NoteEncryptionService.Encrypt(
                new NoteContent("", Convert.ToBase64String(bytes)),
                key);
            File.WriteAllText(
                encryptedPath,
                JsonSerializer.Serialize(payload, JsonOptions),
                Utf8NoBom);
            File.Delete(file);
            var oldReference = $"{ImagesDirectoryName}/{Path.GetFileName(file)}";
            var newReference =
                $"{ImagesDirectoryName}/{Path.GetFileName(encryptedPath)}";
            content = content.Replace(
                oldReference,
                newReference,
                StringComparison.OrdinalIgnoreCase);
            richTextData = richTextData?.Replace(
                oldReference,
                newReference,
                StringComparison.OrdinalIgnoreCase);
        }

        content = DataImageRegex.Replace(content, match =>
        {
            var url = match.Groups["url"].Value;
            var comma = url.IndexOf(',');
            if (comma < 0)
            {
                return match.Value;
            }

            try
            {
                var bytes = Convert.FromBase64String(url[(comma + 1)..]);
                var fileName =
                    $"image-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png.enc";
                var path = Path.Combine(imagesDirectory, fileName);
                var payload = NoteEncryptionService.Encrypt(
                    new NoteContent("", Convert.ToBase64String(bytes)),
                    key);
                File.WriteAllText(
                    path,
                    JsonSerializer.Serialize(payload, JsonOptions),
                    Utf8NoBom);
                return $"![图片]({ImagesDirectoryName}/{fileName})";
            }
            catch
            {
                return match.Value;
            }
        });
        if (richTextData is not null)
        {
            richTextData = DataImageRegex.Replace(richTextData, match =>
            {
                var url = match.Groups["url"].Value;
                var comma = url.IndexOf(',');
                if (comma < 0)
                {
                    return match.Value;
                }

                try
                {
                    var bytes = Convert.FromBase64String(url[(comma + 1)..]);
                    var fileName =
                        $"image-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.png.enc";
                    var path = Path.Combine(imagesDirectory, fileName);
                    var payload = NoteEncryptionService.Encrypt(
                        new NoteContent("", Convert.ToBase64String(bytes)),
                        key);
                    File.WriteAllText(
                        path,
                        JsonSerializer.Serialize(payload, JsonOptions),
                        Utf8NoBom);
                    return $"![图片]({ImagesDirectoryName}/{fileName})";
                }
                catch
                {
                    return match.Value;
                }
            });
        }

        return new NoteContent("", content, richTextData);
    }

    private static string ComposeMarkdownFile(
        string content,
        string? richTextData)
    {
        if (string.IsNullOrWhiteSpace(richTextData))
        {
            return content;
        }

        var encoded = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(richTextData));
        return $"{RichTextMarkerPrefix}{encoded}{RichTextMarkerSuffix}\n{content}";
    }

    private static NoteContent ParseMarkdownFile(string fileContent)
    {
        if (!fileContent.StartsWith(
                RichTextMarkerPrefix,
                StringComparison.Ordinal))
        {
            return new NoteContent("", fileContent);
        }

        var markerEnd = fileContent.IndexOf(
            RichTextMarkerSuffix,
            RichTextMarkerPrefix.Length,
            StringComparison.Ordinal);
        if (markerEnd < 0)
        {
            return new NoteContent("", fileContent);
        }

        try
        {
            var encoded = fileContent[
                RichTextMarkerPrefix.Length..markerEnd];
            var richTextData = Encoding.UTF8.GetString(
                Convert.FromBase64String(encoded));
            var contentStart = markerEnd + RichTextMarkerSuffix.Length;
            if (contentStart < fileContent.Length &&
                fileContent[contentStart] == '\r')
            {
                contentStart++;
            }
            if (contentStart < fileContent.Length &&
                fileContent[contentStart] == '\n')
            {
                contentStart++;
            }
            return new NoteContent(
                "",
                fileContent[contentStart..],
                richTextData);
        }
        catch
        {
            return new NoteContent("", fileContent);
        }
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = title.Trim();
        return normalized.Length == 0 ? "未命名笔记" : normalized;
    }

    private static string SanitizePathSegment(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var builder = new StringBuilder();
        foreach (var character in name.Trim())
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }
        var sanitized = builder.ToString().Trim().TrimEnd('.', ' ');
        if (sanitized.Length == 0)
        {
            sanitized = "未命名笔记";
        }
        if (sanitized.Length > 80)
        {
            sanitized = sanitized[..80].TrimEnd('.', ' ');
        }
        var reserved = new HashSet<string>(
            new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5",
                "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5",
                "LPT6", "LPT7", "LPT8", "LPT9"
            },
            StringComparer.OrdinalIgnoreCase);
        return reserved.Contains(sanitized)
            ? "_" + sanitized
            : sanitized;
    }

    private NoteIndexEntry? FindEntry(NotesIndex index, string noteId) =>
        index.Notes.FirstOrDefault(entry => entry.Id == noteId);

    private NoteIndexEntry? TryGetCachedEntry(string noteId)
    {
        lock (_locationLock)
        {
            return _locations.TryGetValue(noteId, out var entry)
                ? entry
                : null;
        }
    }

    private string? TryGetCachedNoteFolder(string noteId) =>
        TryGetCachedEntry(noteId) is { } entry
            ? GetNoteFolder(entry)
            : null;

    private void CacheLocations(NotesIndex index)
    {
        lock (_locationLock)
        {
            _locations.Clear();
            foreach (var entry in index.Notes)
            {
                _locations[entry.Id] = entry;
            }
        }
    }

    private void DeleteLegacyNoteFile(NoteDocument note)
    {
        var path = Path.Combine(NotesDirectory, $"{note.Id}.note.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.LogException("清理笔记附件", exception);
        }
    }

    private static async Task WriteTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken,
        Action<string>? validate = null)
    {
        var bytes = Utf8NoBom.GetBytes(content);
        await WriteBytesAtomicallyAsync(
            path,
            bytes,
            cancellationToken,
            validate);
    }

    private static async Task WriteBytesAtomicallyAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken,
        Action<string>? validateText = null)
    {
        if (bytes.Length > 0 && bytes.All(value => value == 0))
        {
            throw new InvalidDataException("拒绝写入全零文件。");
        }

        var directory = Path.GetDirectoryName(path)
                        ?? throw new InvalidOperationException("文件路径无效。");
        Directory.CreateDirectory(directory);
        var temporaryPath =
            path + $".{Guid.NewGuid():N}.tmp";
        var backupPath = path + ".bak";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous |
                             FileOptions.WriteThrough |
                             FileOptions.SequentialScan))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            var written = await File.ReadAllBytesAsync(
                temporaryPath,
                cancellationToken);
            if (written.Length != bytes.Length ||
                !written.AsSpan().SequenceEqual(bytes))
            {
                throw new IOException("文件写入校验失败。");
            }

            if (validateText is not null)
            {
                validateText(Utf8NoBom.GetString(written));
            }

            if (File.Exists(path))
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                File.Replace(
                    temporaryPath,
                    path,
                    backupPath,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
            }
            throw;
        }
    }

    private static void ValidateJsonDocument(string json)
    {
        using var document = JsonDocument.Parse(json);
    }

    private static void ValidateIndexDocument(string json)
    {
        var parsed = JsonSerializer.Deserialize<NotesIndex>(
                         json,
                         JsonOptions)
                     ?? throw new InvalidDataException("笔记索引为空。");
        if (parsed.Version < 1)
        {
            throw new InvalidDataException("笔记索引版本无效。");
        }
    }

    private static void ValidateNote(NoteDocument note)
    {
        ValidateId(note.Id);
        if (note.Title.Length > MaxTitleLength)
        {
            throw new InvalidOperationException($"笔记标题不能超过 {MaxTitleLength} 个字符。");
        }
        if (note.Content.Length > MaxContentLength)
        {
            throw new InvalidOperationException("笔记内容过大，无法保存。");
        }
    }

    private static bool IsValidId(string noteId) =>
        noteId.Length == 32 &&
        noteId.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void ValidateId(string noteId)
    {
        if (!IsValidId(noteId))
        {
            throw new InvalidDataException("笔记或目录 ID 无效。");
        }
    }
}
