using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class LlmSummaryCacheService
{
    private const int CacheFormatVersion = 4;
    private const int MaximumEntries = 300;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromDays(90);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly string _cachePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public LlmSummaryCacheService(string llmRootDirectory)
    {
        Directory.CreateDirectory(llmRootDirectory);
        _cachePath = Path.Combine(llmRootDirectory, "summary-cache.json");
    }

    public async Task<string?> GetAsync(
        string cacheKey,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            return document.Entries.TryGetValue(cacheKey, out var entry)
                ? entry.Text
                : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(
        string cacheKey,
        string text,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var document = await LoadAsync(cancellationToken);
            document.Entries[cacheKey] = new LlmSummaryCacheEntry
            {
                Text = text,
                CreatedAt = DateTimeOffset.Now
            };

            var threshold = DateTimeOffset.Now - MaximumAge;
            var entriesToRemove = document.Entries
                .Where(pair => pair.Value.CreatedAt < threshold)
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in entriesToRemove)
            {
                document.Entries.Remove(key);
            }

            if (document.Entries.Count > MaximumEntries)
            {
                foreach (var pair in document.Entries
                             .OrderBy(pair => pair.Value.CreatedAt)
                             .Take(document.Entries.Count - MaximumEntries))
                {
                    document.Entries.Remove(pair.Key);
                }
            }

            var temporaryPath = _cachePath + ".tmp";
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(document, _jsonOptions),
                Utf8NoBom,
                cancellationToken);
            File.Move(temporaryPath, _cachePath, overwrite: true);
        }
        finally
        {
            _lock.Release();
        }
    }

    public static string CreateKey(
        string packageId,
        string promptTemplate,
        string fullName,
        string description,
        IReadOnlyList<string> topics,
        string readmeSummary)
    {
        var input = string.Join(
            "\n",
            CacheFormatVersion,
            packageId,
            promptTemplate,
            fullName,
            description,
            string.Join(",", topics),
            readmeSummary);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    private async Task<LlmSummaryCacheDocument> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_cachePath))
        {
            return new LlmSummaryCacheDocument();
        }

        try
        {
            return JsonSerializer.Deserialize<LlmSummaryCacheDocument>(
                       await File.ReadAllTextAsync(_cachePath, cancellationToken),
                       _jsonOptions)
                   ?? new LlmSummaryCacheDocument();
        }
        catch (JsonException)
        {
            return new LlmSummaryCacheDocument();
        }
    }
}

internal sealed class LlmSummaryCacheDocument
{
    public Dictionary<string, LlmSummaryCacheEntry> Entries { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class LlmSummaryCacheEntry
{
    public string Text { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}
