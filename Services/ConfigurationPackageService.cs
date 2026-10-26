using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class ConfigurationPackageService
{
    public const string FormatId = "GitHubTopicReports.Configuration";
    public const int FormatVersion = 1;

    private const long MaxImportBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxImportEntries = 10_000;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly string[] IncludedDataFiles =
    [
        "favorites.json",
        "搜索记录.txt",
        "star-snapshots.jsonl",
        "global-daily-ranks.zip"
    ];

    public ConfigurationPackageService(string libraryRoot)
    {
        LibraryRoot = Path.GetFullPath(libraryRoot);
    }

    public string LibraryRoot { get; }

    public async Task<ConfigurationExportResult> ExportAsync(
        string destinationPath,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Path.GetFileName(destinationPath)))
        {
            throw new InvalidOperationException("请选择有效的配置包保存位置。");
        }

        var fullDestination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(fullDestination)
            ?? throw new InvalidOperationException("配置包保存路径无效。");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = fullDestination + ".tmp";
        var fileCount = 0;
        long totalBytes = 0;

        try
        {
            await using var output = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

            var manifest = JsonSerializer.SerializeToUtf8Bytes(new
            {
                format = FormatId,
                formatVersion = FormatVersion,
                appVersion,
                exportedAt = DateTimeOffset.Now,
                excludes = new[] { "github-token.txt", "error.log", "Backups" }
            }, new JsonSerializerOptions { WriteIndented = true });
            AddBytes(archive, "manifest.json", manifest, ref fileCount, ref totalBytes);

            var dataDirectory = Path.Combine(LibraryRoot, "Data");
            foreach (var fileName in IncludedDataFiles)
            {
                var sourcePath = Path.Combine(dataDirectory, fileName);
                if (File.Exists(sourcePath))
                {
                    AddFile(archive, $"Data/{fileName}", sourcePath, ref fileCount, ref totalBytes);
                }
            }

            var reportsDirectory = Path.Combine(LibraryRoot, "Reports");
            if (Directory.Exists(reportsDirectory))
            {
                foreach (var reportPath in Directory
                             .EnumerateFiles(reportsDirectory, "*.txt", SearchOption.TopDirectoryOnly)
                             .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AddFile(
                        archive,
                        $"Reports/{Path.GetFileName(reportPath)}",
                        reportPath,
                        ref fileCount,
                        ref totalBytes);
                }
            }

            // Keep compatibility with data produced by 1.4.x.
            var legacySearchDirectory = Path.Combine(LibraryRoot, "SearchLibrary");
            if (Directory.Exists(legacySearchDirectory))
            {
                foreach (var legacyPath in Directory.EnumerateFiles(
                             legacySearchDirectory,
                             "*.txt",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = Path.GetRelativePath(LibraryRoot, legacyPath)
                        .Replace('\\', '/');
                    AddFile(archive, relative, legacyPath, ref fileCount, ref totalBytes);
                }
            }

            archive.Dispose();
            await output.FlushAsync(cancellationToken);
            await output.DisposeAsync();
            File.Move(temporaryPath, fullDestination, overwrite: true);

            return new ConfigurationExportResult
            {
                FilePath = fullDestination,
                FileCount = fileCount,
                TotalBytes = totalBytes
            };
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    public async Task<ConfigurationImportResult> ImportAsync(
        string packagePath,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(packagePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("找不到要导入的配置文件。", sourcePath);
        }

        await using var input = File.OpenRead(sourcePath);
        using var sourceArchive = new ZipArchive(input, ZipArchiveMode.Read);
        ValidateArchive(sourceArchive);

        var manifestEntry = sourceArchive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("配置包缺少 manifest.json。");
        await ValidateManifestAsync(manifestEntry, cancellationToken);

        var backupsDirectory = Path.Combine(LibraryRoot, "Backups");
        Directory.CreateDirectory(backupsDirectory);
        var backupPath = Path.Combine(
            backupsDirectory,
            $"before-import-{DateTime.Now:yyyyMMddHHmmss}.ghconfig");
        await ExportAsync(backupPath, appVersion, cancellationToken);

        var stagingRoot = Path.Combine(
            LibraryRoot,
            $".import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);
        var importedFileCount = 0;
        long importedBytes = 0;

        try
        {
            foreach (var entry in sourceArchive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var normalizedName = entry.FullName.Replace('\\', '/');
                if (!IsImportableEntry(normalizedName) ||
                    normalizedName.EndsWith("/", StringComparison.Ordinal))
                {
                    continue;
                }

                var destinationPath = SafeCombine(stagingRoot, normalizedName);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using (var entryStream = entry.Open())
                await using (var destinationStream = new FileStream(
                                 destinationPath,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81920,
                                 useAsync: true))
                {
                    await entryStream.CopyToAsync(destinationStream, cancellationToken);
                }

                importedFileCount++;
                importedBytes += entry.Length;
            }

            ReplaceLibraryContent(stagingRoot, backupPath);
            return new ConfigurationImportResult
            {
                ImportedFileCount = importedFileCount,
                ImportedBytes = importedBytes,
                AutomaticBackupPath = backupPath
            };
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > MaxImportEntries)
        {
            throw new InvalidDataException("配置包文件数量异常，已停止导入。");
        }

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            totalBytes += entry.Length;
        }

        if (totalBytes > MaxImportBytes)
        {
            throw new InvalidDataException("配置包解压后体积过大，已停止导入。");
        }
    }

    private static async Task ValidateManifestAsync(
        ZipArchiveEntry manifestEntry,
        CancellationToken cancellationToken)
    {
        await using var stream = manifestEntry.Open();
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("format", out var format) ||
            format.GetString() != FormatId ||
            !root.TryGetProperty("formatVersion", out var versionElement) ||
            !versionElement.TryGetInt32(out var version) ||
            version > FormatVersion)
        {
            throw new InvalidDataException("不是有效的 GitHubTopicReports 配置文件，或版本过新。");
        }
    }

    private void ReplaceLibraryContent(string stagingRoot, string backupPath)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var rollbackRoot = Path.Combine(LibraryRoot, "ImportTrash", timestamp);
        Directory.CreateDirectory(rollbackRoot);

        var names = new[] { "Data", "Reports", "SearchLibrary" };
        var movedOldDirectories = new List<(string Name, string OriginalPath)>();
        try
        {
            foreach (var name in names)
            {
                var currentPath = Path.Combine(LibraryRoot, name);
                if (Directory.Exists(currentPath))
                {
                    var rollbackPath = Path.Combine(rollbackRoot, name);
                    Directory.Move(currentPath, rollbackPath);
                    movedOldDirectories.Add((name, rollbackPath));
                }

                var stagedPath = Path.Combine(stagingRoot, name);
                if (Directory.Exists(stagedPath))
                {
                    Directory.Move(stagedPath, currentPath);
                }
                else
                {
                    Directory.CreateDirectory(currentPath);
                }
            }
        }
        catch
        {
            foreach (var name in names)
            {
                var targetPath = Path.Combine(LibraryRoot, name);
                if (Directory.Exists(targetPath))
                {
                    var failedPath = Path.Combine(
                        rollbackRoot,
                        $"failed-{name}-{Guid.NewGuid():N}");
                    Directory.Move(targetPath, failedPath);
                }
            }

            foreach (var (_, originalPath) in movedOldDirectories)
            {
                var name = Path.GetFileName(originalPath);
                var targetPath = Path.Combine(LibraryRoot, name);
                if (Directory.Exists(originalPath))
                {
                    Directory.Move(originalPath, targetPath);
                }
            }

            throw new InvalidOperationException(
                $"导入失败，已尝试恢复原数据。自动备份：{backupPath}");
        }
    }

    private static bool IsImportableEntry(string entryName)
    {
        return entryName == "manifest.json" ||
               entryName.StartsWith("Data/", StringComparison.Ordinal) ||
               entryName.StartsWith("Reports/", StringComparison.Ordinal) ||
               entryName.StartsWith("SearchLibrary/", StringComparison.Ordinal);
    }

    private static string SafeCombine(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var combined = Path.GetFullPath(
            Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("配置包包含不安全的文件路径。");
        }

        return combined;
    }

    private static void AddFile(
        ZipArchive archive,
        string entryName,
        string sourcePath,
        ref int fileCount,
        ref long totalBytes)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var output = entry.Open();
        using var input = File.OpenRead(sourcePath);
        input.CopyTo(output);
        fileCount++;
        totalBytes += input.Length;
    }

    private static void AddBytes(
        ZipArchive archive,
        string entryName,
        byte[] content,
        ref int fileCount,
        ref long totalBytes)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(content);
        fileCount++;
        totalBytes += content.LongLength;
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
            // Best effort cleanup.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Staging directories are safe to keep for troubleshooting.
        }
    }
}
