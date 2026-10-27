<p align="center">
  <img src="docs/assets/readme-banner.svg" alt="Local GitHub Library — 本地优先的 GitHub 项目工作台" width="100%">
</p>

<h1 align="center">Local GitHub Library</h1>

<p align="center">
  <strong>发现值得关注的项目，观察 Star 增长，并把它们整理成自己的本地资料库。</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-0.2.0-ff7a45?style=flat-square" alt="Version 0.2.0">
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-111816?style=flat-square&logo=windows&logoColor=white" alt="Windows 10/11 x64">
  <img src="https://img.shields.io/badge/runtime-.NET%2010-512bd4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/data-local--first-c5f36b?style=flat-square" alt="Local-first">
</p>

<p align="center">
  <a href="README.en.md">English</a>
  ·
  <strong>简体中文</strong>
  ·
  <a href="README.zh-TW.md">繁體中文</a>
</p>

<p align="center">
  <a href="https://github.com/huaaorain/local-github-library/releases/latest"><strong>下载最新版本</strong></a>
  ·
  <a href="#核心能力">核心能力</a>
  ·
  <a href="#快速开始">快速开始</a>
  ·
  <a href="#所有版本">所有版本</a>
  ·
  <a href="https://github.com/huaaorain/local-github-library/issues">反馈问题</a>
</p>

---

## 解决的问题

GitHub 适合找到一个仓库，却不擅长回答跨主题、跨时间的问题：

- 某个技术领域当前 Star 最高的项目有哪些？
- 持续关注的项目，一周或更久以后增长了多少？
- 全站最近一天、一周或一年，哪些项目正在快速上升？
- 如何把搜索结果、收藏和 Star 快照长期保存在自己的电脑上？

**Local GitHub Library** 把这些任务放进一个无需数据库、无需云端账户的 Windows 桌面工具中。业务数据使用 TXT、JSON、JSONL 和 ZIP 等普通文件，便于查看、备份和迁移。

## 核心能力

<table>
  <colgroup>
    <col width="160">
    <col>
  </colgroup>
  <thead>
    <tr>
      <th>模块</th>
      <th>能做什么</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td><strong>主&#8239;题&#8239;发&#8239;现</strong></td>
      <td>按关键词搜索 GitHub 仓库，查看 Star、Fork、Issues、语言、License、Topics 与更新时间，并生成可阅读的 TXT 报告。</td>
    </tr>
    <tr>
      <td><strong>Star&#8239;趋&#8239;势</strong></td>
      <td>为同一主题保存历史快照；间隔至少 7 天后，可在本地计算 Star 增长排行。</td>
    </tr>
    <tr>
      <td><strong>全&#8239;站&#8239;榜&#8239;单</strong></td>
      <td>查看日、周、月、双月、半年和年的 Star 增量榜，也可单独查看当前 Star 总榜。</td>
    </tr>
    <tr>
      <td><strong>本&#8239;地&#8239;收&#8239;藏</strong></td>
      <td>创建自定义目录，搜索、批量收藏并跳过重复仓库，把项目长期整理成个人资料库。</td>
    </tr>
    <tr>
      <td><strong>项&#8239;目&#8239;理&#8239;解</strong></td>
      <td>从 README 提取项目概述；可选本地 LLM 生成中文摘要与翻译，无需云端推理服务。</td>
    </tr>
    <tr>
      <td><strong>数&#8239;据&#8239;迁&#8239;移</strong></td>
      <td>将报告、查询记录、收藏、Star 快照和榜单缓存打包为 `.ghconfig`，导入前自动备份。</td>
    </tr>
  </tbody>
</table>

## 功能流程

![Local GitHub Library 功能流程示意](docs/assets/workflow.svg)

_上图是依据当前源码与公开功能说明绘制的流程示意，不是软件界面截图。_

从 GitHub API 与公开日榜获取数据，在工作台中形成搜索、趋势、收藏和摘要，再保存为本地文件。网络数据用于发现，本地快照与报告负责持续跟踪。

## 快速开始

### 1. 下载

| 发布包 | 大小 | 适用场景 |
| --- | ---: | --- |
| [标准版 Windows x64](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-win-x64.zip) | 约 77 MB | 搜索、排行、收藏与 README 摘要 |
| [完整离线版](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-full-win-x64.zip) | 约 1.21 GB | 额外包含 Qwen2.5 1.5B 本地模型 |

### 2. 解压并运行

完整解压到有写入权限的普通目录，运行 `GitHubTopicReports.exe`。

> [!IMPORTANT]
> 请保留 EXE 同级的 `runtimes` 目录。不要只移动或复制 EXE 文件。

### 3. 按需配置 GitHub Token

匿名 GitHub API 可用，但额度较低。需要更稳定的搜索和榜单刷新时，在 EXE 旁创建 `github-token.txt`，文件内只填写只读 Token。

```powershell
# 也可以使用环境变量
$env:GITHUB_TOKEN = "your_token"
```

请勿提交或分享 Token。

## 本地 LLM

`v0.2.0` 引入可选 `.ghllm` 扩展包。安装后，仓库详情可以流式生成中文翻译和中文总结；未安装时，搜索、排行、收藏与 README 摘要仍可正常使用。

<details>
<summary><strong>模型要求与提示词机制</strong></summary>

- 推荐模型：Qwen2.5 1.5B Instruct，Q4_K_M 量化
- 模型大小：约 1.12 GB
- 推荐可用内存：6–8 GB
- 首次加载：约 2–10 秒
- 摘要与翻译：约 8–10 秒
- 推理运行时随主程序发布，不需要 OpenAI API Key

模型包与提示词分开管理。提示词可导入、编辑、保存和导出，并支持：

```text
{{FullName}}      # 必填
{{Description}}
{{Topics}}
{{ReadmeSummary}}
```

完整打包与校验规则见 [docs/LLM_PACKAGE.md](docs/LLM_PACKAGE.md)。

</details>

## 数据与隐私

程序在 EXE 旁维护 `GitHubReports/`：

```text
GitHubReports/
  Reports/               # 主题、收藏与总榜报告
  Data/
    搜索记录.txt
    star-snapshots.jsonl
    favorites.json
    global-daily-ranks.zip
    error.log
  Backups/               # 导入配置前自动生成
  LLM/
    Packages/            # 可选本地模型
    prompt.txt
```

- GitHub Token 只从环境变量或 `github-token.txt` 读取。
- `.ghconfig` 不导出 Token 和错误日志。
- 导入前自动备份，并校验格式、体积与路径安全。
- 报告、收藏和 Star 快照不会上传到第三方分析或推理服务。

## 所有版本

<!-- release-catalog:start -->
最新版本: [Local GitHub Library 0.2.0](https://github.com/huaaorain/local-github-library/releases/tag/v0.2.0)

| 版本 | 发布时间 | 发布说明 |
| --- | --- | --- |
| `v0.2.0` | 2026-10-26 | [打开](https://github.com/huaaorain/local-github-library/releases/tag/v0.2.0) |
| `v0.1.0` | 2026-10-26 | [打开](https://github.com/huaaorain/local-github-library/releases/tag/v0.1.0) |

<details>
<summary><strong>全部版本下载</strong></summary>

### Local GitHub Library 0.2.0

发布说明: [打开](https://github.com/huaaorain/local-github-library/releases/tag/v0.2.0)

- [GitHubTopicReports.exe](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/GitHubTopicReports.exe) — 70.6 MB
- [LocalGithubLibrary-0.2.0-full-win-x64.zip](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-full-win-x64.zip) — 1.1 GB
- [LocalGithubLibrary-0.2.0-source.zip](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-source.zip) — 75.3 KB
- [LocalGithubLibrary-0.2.0-win-x64.zip](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/LocalGithubLibrary-0.2.0-win-x64.zip) — 73.2 MB
- [Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm) — 1.0 GB
- [RELEASE_NOTES_0.2.0.md](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/RELEASE_NOTES_0.2.0.md) — 3.1 KB
- [SHA256SUMS.txt](https://github.com/huaaorain/local-github-library/releases/download/v0.2.0/SHA256SUMS.txt) — 598 B
- [源代码（zip）](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.2.0.zip)
- [源代码（tar.gz）](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.2.0.tar.gz)

### Local GitHub Library 0.1.0

发布说明: [打开](https://github.com/huaaorain/local-github-library/releases/tag/v0.1.0)

- [GitHubTopicReports-v0.1.0-win-x64.exe](https://github.com/huaaorain/local-github-library/releases/download/v0.1.0/GitHubTopicReports-v0.1.0-win-x64.exe) — 61.9 MB
- [源代码（zip）](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.1.0.zip)
- [源代码（tar.gz）](https://github.com/huaaorain/local-github-library/archive/refs/tags/v0.1.0.tar.gz)

</details>
<!-- release-catalog:end -->

## 系统要求

- Windows 10 或 Windows 11，64 位
- Intel 或 AMD 处理器
- 可访问 `github.com` 与 `api.github.com`
- 搜索、README 读取与当前榜单需要联网
- 收藏浏览和已下载的榜单缓存可离线使用

## 已知限制

- GitHub Search API 最多返回 1000 条结果，匿名请求额度较低。
- 全站增量榜依赖 [OpenGithubs 公开日榜](https://github.com/OpenGithubs/github-daily-rank) 的覆盖范围。
- 当前只提供 Windows x64 构建。
- 未签名 EXE 可能触发 Windows SmartScreen 提示。
- 当前仓库尚未声明开源许可证。

## 从源码构建

<details>
<summary><strong>构建、运行与发布</strong></summary>

```powershell
dotnet restore
dotnet build .\GitHubTopicReports.csproj -c Release
dotnet run --project .\GitHubTopicReports.csproj
```

发布 Windows x64 自包含版本：

```powershell
.\publish.ps1
```

发布脚本会生成单文件主程序，并将 LLamaSharp 的 CPU 原生运行库复制到 `dist\runtimes\win-x64\native`。分发时必须包含该目录。

</details>

## 参与贡献

欢迎提交 Issue 和 Pull Request。提交前请确认：

- 不包含 Token、个人报告、收藏或本地日志。
- 已完成 Release 构建。
- 修改配置格式时同步升级 `formatVersion`。

## 更新记录

- [0.2.0：本地 LLM、提示词管理与收藏体验修复](docs/releases/v0.2.0.md)
- [0.1.0：首个公开版本](docs/releases/v0.1.0.md)
- 完整历史见 [CHANGELOG.md](CHANGELOG.md)

## 致谢

- [GitHub REST API](https://docs.github.com/rest)
- [OpenGithubs GitHub Daily Rank](https://github.com/OpenGithubs/github-daily-rank)
- [.NET](https://dotnet.microsoft.com/) 与 WPF
- [LLamaSharp](https://github.com/SciSharp/LLamaSharp) 与 [llama.cpp](https://github.com/ggml-org/llama.cpp)
- Qwen2.5 模型（Apache-2.0）

## License

当前仓库尚未指定开源许可证。在添加许可证前，请勿直接将代码作为商业或再分发项目的一部分使用。
