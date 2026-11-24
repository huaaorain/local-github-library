using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace GitHubTopicReports.Models;

public sealed class NoteDocument
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string? RichTextData { get; set; }
    [JsonIgnore]
    public string? RelativeFolderPath { get; set; }
    public string? FolderId { get; set; }
    public int SortOrder { get; set; } = int.MaxValue;
    public EncryptedNotePayload? Encrypted { get; set; }

    [JsonIgnore]
    public bool IsEncrypted => Encrypted is not null;

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title)
        ? "未命名笔记"
        : Title.Trim();

    [JsonIgnore]
    public string UpdatedDisplay => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

public sealed class EncryptedNotePayload
{
    public int Version { get; set; } = 1;
    public int Iterations { get; set; }
    public string Salt { get; set; } = "";
    public string Nonce { get; set; } = "";
    public string Ciphertext { get; set; } = "";
    public string Tag { get; set; } = "";
}

public sealed record NoteContent(
    string Title,
    string Content,
    string? RichTextData = null);

public sealed class NoteRichTextData
{
    public int Version { get; set; } = 1;
    public List<NoteRichInline> Inlines { get; set; } = [];
}

public sealed class NoteRichInline
{
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public string? ImageReference { get; set; }
    public string? FontFamily { get; set; }
    public string? FontWeight { get; set; }
    public string? FontStyle { get; set; }
    public string? FontStretch { get; set; }
    public string? Foreground { get; set; }
}

public sealed class NoteFolder
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ParentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class NotesIndex
{
    public int Version { get; set; } = 2;
    public List<NoteFolder> Folders { get; set; } = [];
    public List<NoteIndexEntry> Notes { get; set; } = [];
}

public sealed class NoteIndexEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string? FolderId { get; set; }
    public int SortOrder { get; set; } = int.MaxValue;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public bool IsEncrypted { get; set; }
}

public sealed class NoteTreeItem
{
    public string Kind { get; init; } = "note";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string UpdatedDisplay { get; init; } = "";
    public bool IsEncrypted { get; init; }
    public bool IsFolder => Kind == "folder";
    public string? FolderId { get; init; }
    public NoteDocument? Note { get; init; }
    public ObservableCollection<NoteTreeItem> Children { get; init; } = [];
}

public sealed class NoteFolderOption
{
    public string? Id { get; init; }
    public required string DisplayName { get; init; }
}
