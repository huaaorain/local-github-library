using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class LlmPackageService
{
    public const string PackageExtension = ".ghllm";
    public const string PackageFormat = "LocalGithubLibrary.LLMPackage";
    public const int PackageFormatVersion = 1;

    private const long MaxModelBytes = 6L * 1024 * 1024 * 1024;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public LlmPackageService(string libraryRoot)
    {
        RootDirectory = Path.Combine(Path.GetFullPath(libraryRoot), "LLM");
        PackagesDirectory = Path.Combine(RootDirectory, "Packages");
        ActivePackageFile = Path.Combine(RootDirectory, "active.json");
        Directory.CreateDirectory(PackagesDirectory);
    }

    public string RootDirectory { get; }
    public string PackagesDirectory { get; }
    public string ActivePackageFile { get; }

    public async Task<InstalledLlmPackage?> GetActivePackageAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ActivePackageFile))
        {
            return null;
        }

        try
        {
            var active = JsonSerializer.Deserialize<ActivePackageRecord>(
                await File.ReadAllTextAsync(ActivePackageFile, cancellationToken),
                _jsonOptions);
            if (active is null || string.IsNullOrWhiteSpace(active.PackageId))
            {
                return null;
            }

            var packageDirectory = Path.Combine(PackagesDirectory, active.PackageId);
            var manifestPath = Path.Combine(packageDirectory, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                return null;
            }

            var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
            var modelPath = SafeCombine(packageDirectory, manifest.ModelFile);
            if (!File.Exists(modelPath))
            {
                return null;
            }

            return new InstalledLlmPackage
            {
                Manifest = manifest,
                PackageDirectory = packageDirectory,
                ModelPath = modelPath,
                ModelSize = new FileInfo(modelPath).Length
            };
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or InvalidDataException)
        {
            AppDiagnostics.LogException("读取本地 LLM 包", exception);
            return null;
        }
    }

    public async Task<LlmPackageImportResult> ImportAsync(
        string packagePath,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                Path.GetExtension(packagePath),
                PackageExtension,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"请选择 {PackageExtension} 格式的 LLM 包。");
        }

        var stagingDirectory = Path.Combine(
            RootDirectory,
            $".import-llm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            await using (var input = File.OpenRead(packagePath))
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
            {
                ValidateArchive(archive);
                var manifestEntry = archive.GetEntry("manifest.json")
                    ?? throw new InvalidDataException("LLM 包缺少 manifest.json。");
                await ExtractEntryAsync(manifestEntry, stagingDirectory, cancellationToken);

                var manifestPath = Path.Combine(stagingDirectory, "manifest.json");
                var manifest = await ReadManifestAsync(manifestPath, cancellationToken);
                ValidateManifest(manifest, appVersion);
                var packageId = ValidatePackageId(manifest.PackageId);

                foreach (var entry in archive.Entries)
                {
                    var normalized = entry.FullName.Replace('\\', '/');
                    if (normalized == "manifest.json" ||
                        normalized.StartsWith("models/", StringComparison.Ordinal) ||
                        normalized.StartsWith("licenses/", StringComparison.Ordinal))
                    {
                        await ExtractEntryAsync(entry, stagingDirectory, cancellationToken);
                    }
                }

                var extractedModelPath = SafeCombine(stagingDirectory, manifest.ModelFile);
                if (!File.Exists(extractedModelPath))
                {
                    throw new InvalidDataException("LLM 包中缺少清单指定的模型文件。");
                }

                var actualHash = await ComputeSha256Async(extractedModelPath, cancellationToken);
                if (!string.Equals(
                        actualHash,
                        manifest.ModelSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("LLM 模型 SHA-256 校验失败，文件可能损坏或被修改。");
                }

                var destinationDirectory = Path.Combine(PackagesDirectory, packageId);
                var replaced = Directory.Exists(destinationDirectory);
                if (replaced)
                {
                    var trashDirectory = Path.Combine(
                        RootDirectory,
                        "Trash",
                        $"{packageId}-{DateTime.Now:yyyyMMddHHmmss}");
                    Directory.CreateDirectory(Path.GetDirectoryName(trashDirectory)!);
                    Directory.Move(destinationDirectory, trashDirectory);
                }

                Directory.Move(stagingDirectory, destinationDirectory);
                await File.WriteAllTextAsync(
                    ActivePackageFile,
                    JsonSerializer.Serialize(
                        new ActivePackageRecord { PackageId = packageId },
                        _jsonOptions),
                    cancellationToken);

                var installed = new InstalledLlmPackage
                {
                    Manifest = manifest,
                    PackageDirectory = destinationDirectory,
                    ModelPath = SafeCombine(destinationDirectory, manifest.ModelFile),
                    ModelSize = new FileInfo(SafeCombine(destinationDirectory, manifest.ModelFile)).Length
                };
                return new LlmPackageImportResult
                {
                    Package = installed,
                    ReplacedExistingPackage = replaced
                };
            }
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        if (archive.Entries.Count > 50)
        {
            throw new InvalidDataException("LLM 包文件数量异常。");
        }

        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var normalized = entry.FullName.Replace('\\', '/');
            if (!(normalized == "manifest.json" ||
                  normalized.StartsWith("models/", StringComparison.Ordinal) ||
                  normalized.StartsWith("licenses/", StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"LLM 包包含不允许的文件：{entry.FullName}");
            }

            totalBytes += entry.Length;
        }

        if (totalBytes > MaxModelBytes)
        {
            throw new InvalidDataException("LLM 包解压后体积超过 6 GB 限制。");
        }
    }

    private static void ValidateManifest(LlmPackageManifest manifest, string appVersion)
    {
        if (manifest.Format != PackageFormat ||
            manifest.FormatVersion != PackageFormatVersion)
        {
            throw new InvalidDataException("不支持的 LLM 包格式或版本。");
        }

        if (string.IsNullOrWhiteSpace(manifest.DisplayName) ||
            string.IsNullOrWhiteSpace(manifest.ModelFile) ||
            manifest.ModelFile.Contains("..", StringComparison.Ordinal) ||
            !manifest.ModelFile.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("LLM 包清单中的模型信息无效。");
        }

        if (manifest.ModelSha256.Length != 64 ||
            !manifest.ModelSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("LLM 包未提供有效的 SHA-256。");
        }

        if (manifest.ContextSize < 1024 || manifest.ContextSize > 32768)
        {
            throw new InvalidDataException("LLM 上下文大小必须在 1024 到 32768 之间。");
        }

        if (Version.TryParse(manifest.MinimumAppVersion, out var minimumVersion) &&
            Version.TryParse(appVersion, out var currentVersion) &&
            currentVersion < minimumVersion)
        {
            throw new InvalidDataException(
                $"此 LLM 包需要 Local Github Library {manifest.MinimumAppVersion} 或更高版本。");
        }
    }

    private async Task<LlmPackageManifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var manifest = JsonSerializer.Deserialize<LlmPackageManifest>(
            await File.ReadAllTextAsync(path, cancellationToken),
            _jsonOptions);
        return manifest ?? throw new InvalidDataException("LLM 包清单内容为空。");
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var destination = SafeCombine(stagingDirectory, entry.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using var input = entry.Open();
        await using var output = new FileStream(
            destination,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
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
            throw new InvalidDataException("LLM 包包含不安全的文件路径。");
        }

        return combined;
    }

    private static string ValidatePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) ||
            packageId.Length > 80 ||
            packageId.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) ||
                  character is '-' or '_' or '.')))
        {
            throw new InvalidDataException("LLM 包 ID 无效。");
        }

        return packageId;
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
            // Best effort cleanup of a private staging directory.
        }
    }

    private sealed class ActivePackageRecord
    {
        public string PackageId { get; init; } = "";
    }
}
