# Changelog

All notable changes to Local Github Library will be documented in this file.

## [0.2.0] - 2026-10-26

### Fixed

- Include the LLamaSharp CPU native runtime beside the self-contained executable.
- Configure the native library search directory before model initialization.
- Fail the Windows publish when required LLamaSharp runtime files are missing.
- Ignore language selectors, navigation lists and README alert blocks when
  extracting project summaries.
- Prevent the local model from echoing prompt instructions and output-format
  guidance into generated project summaries.
- Disable favorite actions when the selected repository already exists in the
  destination folder.
- Populate the Find-project destination folder list from the same saved folder
  data used by My Favorites.
- Vertically center the result-count and favorite-folder ComboBox contents.

### Added

- Optional local LLM package support using the `.ghllm` format.
- Embedded llama.cpp CPU inference runtime through LLamaSharp.
- Separate editable LLM prompt files with TXT import and export.
- Centered LLM settings dialog for model package and prompt management.
- Prompt-content-aware LLM result caching.
- SHA-256 validation and manifest validation for imported model packages.
- Qwen2.5 1.5B Instruct Q4_K_M package builder script.
- Streaming Chinese translation and summary generation in all repository detail views.
- Model package status and import UI.
- Graceful README-summary fallback when no LLM package is installed.

## [0.1.0] - 2026-10-26

### Added

- GitHub keyword repository search with configurable result count.
- Current Star ranking and locally calculated weekly growth.
- Full-site Star leaderboard with daily, weekly, monthly, two-month, half-year and yearly periods.
- Separate incremented and current Star leaderboard modes.
- Stable current-Star snapshots so count changes do not reshuffle the leading results.
- Saved leaderboard history in the leaderboard workspace.
- Favorite directory creation, renaming, deduplication and removal.
- GitHub search integrated directly into the favorites workspace.
- Batch favoriting of selected or all search results.
- Repository detail views for search results, favorites and leaderboard entries.
- README-based project summaries with metadata fallback.
- Local TXT reports using timestamp filenames.
- `.ghconfig` ZIP configuration export and safe import with automatic backup.
- Direct TLS connection handling for GitHub API requests.
- User-facing network diagnostics and error logs.
- Self-contained Windows x64 single-file build.
