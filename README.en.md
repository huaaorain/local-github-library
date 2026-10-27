<p align="center">
  <img src="docs/assets/readme-banner.svg" alt="Local GitHub Library — a local-first GitHub project workspace" width="100%">
</p>

<h1 align="center">Local GitHub Library</h1>

<p align="center">
  <strong>Discover projects worth following, track Star growth, and organize them into your own local library.</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.2.0-ff7a45?style=flat-square" alt="Version 0.2.0">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-111816?style=flat-square&logo=windows&logoColor=white" alt="Windows 10/11 x64">
  <img src="https://img.shields.io/badge/runtime-.NET%2010-512bd4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/data-local--first-c5f36b?style=flat-square" alt="Local-first">
</p>

<p align="center">
  <strong>English</strong>
  ·
  <a href="README.md">简体中文</a>
  ·
  <a href="README.zh-TW.md">繁體中文</a>
</p>

<p align="center">
  <a href="https://github.com/huaaorain/local-github-library/releases/latest"><strong>Download latest</strong></a>
  ·
  <a href="#core-capabilities">Core capabilities</a>
  ·
  <a href="#quick-start">Quick start</a>
  ·
  <a href="#all-releases">All releases</a>
  ·
  <a href="https://github.com/huaaorain/local-github-library/issues">Report an issue</a>
</p>

---

## Why it exists

GitHub is good at finding one repository, but it is not designed to answer questions across topics and time:

- Which repositories in a technology area currently have the most Stars?
- How much has a project you follow grown after a week or longer?
- Which projects across GitHub are rising fastest today, this week, or this year?
- How can search results, favorites, and Star snapshots stay on your own computer?

**Local GitHub Library** brings those tasks into a database-free Windows desktop tool that requires no cloud account. Business data is stored in readable TXT, JSON, JSONL, and ZIP files that are easy to inspect, back up, and move.

## Core capabilities

<table>
  <colgroup>
    <col width="160">
    <col>
  </colgroup>
  <thead>
    <tr>
      <th>Module</th>
      <th>What it does</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td><strong>Topic search</strong></td>
      <td>Search GitHub repositories by keyword, inspect Stars, forks, issues, language, license, topics, and update time, then generate a readable TXT report.</td>
    </tr>
    <tr>
      <td><strong>Star trends</strong></td>
      <td>Save historical snapshots for a topic and calculate a local Star-growth ranking after at least seven days.</td>
    </tr>
    <tr>
      <td><strong>Rankings</strong></td>
      <td>View Star-growth rankings for a day, week, month, two months, half year, or year, and inspect the current all-site Star ranking separately.</td>
    </tr>
    <tr>
      <td><strong>Favorites</strong></td>
      <td>Create custom folders, search and batch-favorite repositories, skip duplicates, and maintain a long-term local library.</td>
    </tr>
    <tr>
      <td><strong>Insights</strong></td>
      <td>Extract project summaries from README files. An optional local LLM can create Chinese summaries and translations without a hosted inference API.</td>
    </tr>
    <tr>
      <td><strong>Local data</strong></td>
      <td>Bundle reports, query history, favorites, Star snapshots, and ranking caches into a `.ghconfig` package with an automatic backup before import.</td>
    </tr>
  </tbody>
</table>

## Workflow

![Local GitHub Library workflow diagram](docs/assets/workflow.svg)

_This is a workflow diagram based on the current source and public feature documentation, not a screenshot of the application._

GitHub APIs and public daily rankings supply discovery data. The workspace turns that data into search results, growth trends, favorites, and summaries, then stores everything in local files. Network access powers discovery; snapshots and reports provide continuity.

## Quick start

### 1. Download

| Package | Size | Best for |
| --- | ---: | --- |
| [Standard Windows x64](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-win-x64.zip) | About 77 MB | Search, rankings, favorites, and README summaries |
| [Full offline package](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-full-win-x64.zip) | About 1.21 GB | Adds the Qwen2.5 1.5B local model |

### 2. Extract and run

Extract the complete archive into a normal directory where you have write access, then run `GitHubTopicReports.exe`.

> [!IMPORTANT]
> Keep the `runtimes` directory beside the executable. Do not move or copy only the EXE.

### 3. Optionally configure a GitHub token

Anonymous GitHub API access works, but the request limit is low. For more reliable search and rankings, create `github-token.txt` beside the executable and place only a read-only token inside it.

```powershell
# The environment variable is also supported
$env:GITHUB_TOKEN = "your_token"
```

Do not commit or share the token.

## Local LLM

`v0.2.0` adds an optional `.ghllm` extension package. After installation, repository details can stream Chinese translations and summaries. Without the package, search, rankings, favorites, and README summaries remain available.

<details>
<summary><strong>Model requirements and prompt behavior</strong></summary>

- Recommended model: Qwen2.5 1.5B Instruct, Q4_K_M
- Model size: about 1.12 GB
- Recommended available memory: 6–8 GB
- Initial model load: about 2–10 seconds
- Summary and translation generation: about 8–10 seconds
- The inference runtime ships with the application; no OpenAI API key is required

Model packages and prompts are managed separately. Prompts can be imported, edited, saved, and exported, and support these placeholders:

```text
{{FullName}}      # required
{{Description}}
{{Topics}}
{{ReadmeSummary}}
```

See [docs/LLM_PACKAGE.md](docs/LLM_PACKAGE.md) for package validation and build details.

</details>

## Data and privacy

The application maintains a `GitHubReports/` directory beside the executable:

```text
GitHubReports/
  Reports/               # Topic, favorites, and ranking reports
  Data/
    搜索记录.txt
    star-snapshots.jsonl
    favorites.json
    global-daily-ranks.zip
    error.log
  Backups/               # Created before configuration imports
  LLM/
    Packages/            # Optional local models
    prompt.txt
```

- GitHub tokens are read only from the environment or `github-token.txt`.
- `.ghconfig` excludes tokens and error logs.
- Imports create a backup and validate format, size, and path safety.
- Reports, favorites, and Star snapshots are not uploaded to third-party analytics or inference services.

## All releases

<!-- release-catalog:start -->
Latest: [Local GitHub Library 0.2.0](https://github.com/huaaorain/local-github-library/releases/tag/v0.2.0)

| Version | Published | Release notes |
| --- | --- | --- |
| `v0.2.0` | 2026-10-26 | [Open](https://github.com/huaaorain/local-github-library/releases/tag/v0.2.0) |
| `v0.1.0` | 2026-10-26 | [Open](https://github.com/huaaorain/local-github-library/releases/tag/v0.1.0) |

<details>
<summary><strong>All version downloads</strong></summary>

### Local GitHub Library 0.2.0

Release notes: [Open](https://github.com/huaaorain/local-github-library/releases/tag/v0.2.0)

- [GitHubTopicReports.exe](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/GitHubTopicReports.exe) — 70.6 MB
- [LocalGithubLibrary-0.2.0-full-win-x64.zip](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-full-win-x64.zip) — 1.1 GB
- [LocalGithubLibrary-0.2.0-source.zip](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-source.zip) — 75.3 KB
- [LocalGithubLibrary-0.2.0-win-x64.zip](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-win-x64.zip) — 73.2 MB
- [Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm) — 1.0 GB
- [RELEASE_NOTES_0.2.0.md](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/RELEASE_NOTES_0.2.0.md) — 3.1 KB
- [SHA256SUMS.txt](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/SHA256SUMS.txt) — 598 B
- [Source code (zip)](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.2.0.zip)
- [Source code (tar.gz)](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.2.0.tar.gz)

### Local GitHub Library 0.1.0

Release notes: [Open](https://github.com/huaaorain/local-github-library/releases/tag/v0.1.0)

- [GitHubTopicReports-v0.1.0-win-x64.exe](https://github.com/huaaorain/local-github-library/releases/download/v0.1.0/GitHubTopicReports-v0.1.0-win-x64.exe) — 61.9 MB
- [Source code (zip)](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.1.0.zip)
- [Source code (tar.gz)](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.1.0.tar.gz)

</details>
<!-- release-catalog:end -->

## Requirements

- Windows 10 or Windows 11, 64-bit
- Intel or AMD processor
- Access to `github.com` and `api.github.com`
- Network access for search, README retrieval, and current rankings
- Favorite browsing and downloaded ranking caches work offline

## Known limitations

- GitHub Search API returns at most 1000 results, and anonymous requests have a low quota.
- The all-site growth ranking depends on the coverage of the public [OpenGithubs daily ranking](https://github.com/OpenGithubs/github-daily-rank).
- Only Windows x64 builds are currently provided.
- The unsigned EXE may trigger a Windows SmartScreen warning.
- The repository does not currently declare an open-source license.

## Build from source

<details>
<summary><strong>Build, run, and publish</strong></summary>

```powershell
dotnet restore
dotnet build .\GitHubTopicReports.csproj -c Release
dotnet run --project .\GitHubTopicReports.csproj
```

Publish a self-contained Windows x64 build:

```powershell
.\publish.ps1
```

The publish script creates a single-file application and copies the LLamaSharp CPU native runtime to `dist\runtimes\win-x64\native`. That directory must be distributed with the application.

</details>

## Contributing

Issues and pull requests are welcome. Before submitting:

- Do not include tokens, personal reports, favorites, or local logs.
- Complete a Release build.
- Increment `formatVersion` when changing the configuration format.

## Release history

- [0.2.0: local LLM, prompt management, and favorites fixes](docs/releases/v0.2.0.md)
- [0.1.0: first public release](docs/releases/v0.1.0.md)
- Full history: [CHANGELOG.md](CHANGELOG.md)

## Acknowledgements

- [GitHub REST API](https://docs.github.com/rest)
- [OpenGithubs GitHub Daily Rank](https://github.com/OpenGithubs/github-daily-rank)
- [.NET](https://dotnet.microsoft.com/) and WPF
- [LLamaSharp](https://github.com/SciSharp/LLamaSharp) and [llama.cpp](https://github.com/ggml-org/llama.cpp)
- Qwen2.5 model (Apache-2.0)

## License

This repository does not currently declare an open-source license. Until a license is added, do not use the code as part of a commercial or redistributed product.
