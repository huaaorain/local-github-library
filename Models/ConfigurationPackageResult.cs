namespace GitHubTopicReports.Models;

public sealed class ConfigurationExportResult
{
    public required string FilePath { get; init; }
    public required int FileCount { get; init; }
    public required long TotalBytes { get; init; }
}

public sealed class ConfigurationImportResult
{
    public required int ImportedFileCount { get; init; }
    public required long ImportedBytes { get; init; }
    public required string AutomaticBackupPath { get; init; }
}
