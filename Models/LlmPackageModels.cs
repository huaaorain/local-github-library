using System.Text.Json.Serialization;

namespace GitHubTopicReports.Models;

public sealed class LlmPackageManifest
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = "";

    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; }

    [JsonPropertyName("packageId")]
    public string PackageId { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("modelFile")]
    public string ModelFile { get; init; } = "";

    [JsonPropertyName("modelSha256")]
    public string ModelSha256 { get; init; } = "";

    [JsonPropertyName("quantization")]
    public string Quantization { get; init; } = "";

    [JsonPropertyName("contextSize")]
    public int ContextSize { get; init; } = 4096;

    [JsonPropertyName("recommendedThreads")]
    public int RecommendedThreads { get; init; }

    [JsonPropertyName("minimumAppVersion")]
    public string MinimumAppVersion { get; init; } = "";

    [JsonPropertyName("license")]
    public string License { get; init; } = "";
}

public sealed class InstalledLlmPackage
{
    public required LlmPackageManifest Manifest { get; init; }
    public required string PackageDirectory { get; init; }
    public required string ModelPath { get; init; }
    public required long ModelSize { get; init; }
}

public sealed class LlmPackageImportResult
{
    public required InstalledLlmPackage Package { get; init; }
    public required bool ReplacedExistingPackage { get; init; }
}
