# Local LLM Package

Local Github Library supports optional local LLM packages for Chinese project summaries and translations.

## Package Format

The `.ghllm` file is a ZIP container containing only data and license files:

```text
model.ghllm
├─ manifest.json
├─ models/
│  └─ model.gguf
└─ licenses/
   └─ MODEL-LICENSE.txt
```

The application runtime does not execute files imported from an LLM package.

Generation prompts are intentionally not stored in `.ghllm` packages. The
active prompt is stored separately at:

```text
GitHubReports/LLM/prompt.txt
```

Use **导入 LLM** in the application to import a model package or a TXT prompt.
The prompt supports `{{FullName}}`, `{{Description}}`, `{{Topics}}` and
`{{ReadmeSummary}}` placeholders. `{{FullName}}` is required.

LLamaSharp CPU native libraries are distributed with the application under:

```text
runtimes/win-x64/native/
```

Keep this directory beside `GitHubTopicReports.exe`. The release must not
contain only the executable.

## Manifest

```json
{
  "format": "LocalGithubLibrary.LLMPackage",
  "formatVersion": 1,
  "packageId": "qwen2.5-1.5b-instruct-q4km",
  "displayName": "Qwen2.5 1.5B Instruct Q4",
  "modelFile": "models/qwen2.5-1.5b-instruct-q4_k_m.gguf",
  "modelSha256": "sha256-of-gguf",
  "quantization": "Q4_K_M",
  "contextSize": 4096,
  "recommendedThreads": 6,
  "minimumAppVersion": "0.2.0",
  "license": "Apache-2.0"
}
```

## Build a Package

```powershell
.\tools\build-llm-package.ps1 `
  -ModelPath .\work\qwen2.5-1.5b-instruct-q4_k_m.gguf `
  -LicensePath .\work\LICENSE `
  -OutputPath .\packages\Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm
```

The builder:

- computes the model SHA-256;
- stores the model without additional lossy conversion;
- includes the model license;
- creates a deterministic manifest;
- packs the result as `.ghllm`.

## Import

Use **导入 LLM** in the application header and select the `.ghllm` file.

The application verifies:

- package format and version;
- file path safety;
- package size;
- model SHA-256;
- required model file.

The installed package is stored under:

```text
GitHubReports/LLM/Packages/
```

The active package is recorded in:

```text
GitHubReports/LLM/active.json
```

## Recommended Model

The default recommendation is:

```text
Qwen2.5-1.5B-Instruct
Q4_K_M
```

Approximate model size: 1.12 GB.

Recommended system memory: 6–8 GB.

The smaller 0.5B model can still be imported if its manifest follows the same format, but Chinese translation and summarization quality will be lower.
