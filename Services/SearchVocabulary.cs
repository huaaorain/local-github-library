using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GitHubTopicReports.Models;

namespace GitHubTopicReports.Services;

public sealed class SearchVocabulary
{
    private static readonly Regex CamelCaseRegex = new(
        "([a-z0-9])([A-Z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly char[] WordSeparators =
        [' ', '\t', '\r', '\n', '-', '_', '/', '\\', '.', ',', ':', ';', '|', '(', ')', '[', ']'];

    private static readonly string[] SeedTerms =
    [
        "android", "angular", "api", "artificial", "automation", "backend", "blockchain",
        "browser", "camera", "cloud", "compiler", "computer", "cryptography", "database",
        "desktop", "django", "docker", "electron", "ethereum", "fastapi", "finance", "flask",
        "flutter", "framework", "frontend", "game", "gamedev", "github", "gitlab", "golang",
        "graphics", "graphql", "huggingface", "image", "interpreter", "javascript", "kotlin",
        "kubernetes", "langchain", "laravel", "learning", "library", "linux", "machine",
        "matplotlib", "mobile", "mongodb", "mysql", "neural", "nextjs", "nodejs", "numpy",
        "opencv", "opengl", "pandas", "postgres", "powershell", "processing", "pytorch",
        "python", "quant", "rag", "rails", "react", "redis", "rendering", "rest", "robotics",
        "rust", "scraping", "sdk", "security", "shader", "simulation", "solana", "spring",
        "sqlite", "svelte", "swift", "tensorflow", "toolkit", "trading", "transformers",
        "typescript", "unity", "unreal", "visualization", "vulkan", "webgl", "windows",
        "winui", "workflow", "wpf"
    ];

    private readonly HashSet<string> _terms = new(StringComparer.OrdinalIgnoreCase);
    private readonly string? _filePath;
    private bool _dirty;

    public SearchVocabulary(string? filePath = null)
    {
        _filePath = filePath;
        foreach (var term in SeedTerms)
        {
            _terms.Add(term);
        }

        Load();
    }

    public IReadOnlyCollection<string> Terms => _terms;

    public void Learn(IEnumerable<RepositoryRecord> repositories)
    {
        foreach (var repository in repositories)
        {
            foreach (var topic in repository.Topics)
            {
                LearnText(topic);
            }

            var repositoryName = repository.FullName
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault() ?? "";
            LearnText(CamelCaseRegex.Replace(repositoryName, "$1 $2"));
        }
    }

    public void Learn(string value)
    {
        AddTerm(value);
    }

    public void SaveIfNeeded()
    {
        if (!_dirty || string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllLines(
                _filePath,
                _terms.Order(StringComparer.OrdinalIgnoreCase),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            _dirty = false;
        }
        catch
        {
            // Search remains usable even when the learned vocabulary cannot be saved.
        }
    }

    private void Load()
    {
        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            return;
        }

        try
        {
            foreach (var line in File.ReadLines(_filePath))
            {
                AddTerm(line);
            }
        }
        catch
        {
            // Ignore an unreadable cache and continue with the built-in vocabulary.
        }
    }

    private void LearnText(string value)
    {
        var normalized = CamelCaseRegex.Replace(value, "$1 $2");
        foreach (var token in normalized.Split(
                     WordSeparators,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            AddTerm(token);
        }
    }

    private void AddTerm(string value)
    {
        var term = value.Trim().ToLowerInvariant();
        if (term.Length is < 3 or > 40 ||
            !term.All(character => char.IsLetterOrDigit(character) || character is '+' or '#') ||
            !_terms.Add(term))
        {
            return;
        }

        _dirty = true;
    }
}
