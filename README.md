# Local Github Library

Local Github Library 是一个面向 Windows 的本地优先 GitHub 项目发现与整理工具。它可以按主题搜索仓库、查看当前 Star 排行、根据本地历史快照计算增长、整理全站 Star 总榜，并通过收藏目录和本地 TXT 报告建立自己的开源项目资料库。

> 当前版本：`0.1.0`

## 为什么使用 Local Github Library

GitHub 自带搜索适合查找单个仓库，但不容易同时回答下面这些问题：

- 某个技术领域现在 Star 最高的项目有哪些？
- 我持续关注的项目过去一段时间增长如何？
- 全站最近一天、一周、一个月或一年哪些项目增长最快？
- 如何把搜索结果按自己的主题目录长期保存？
- 如何把全部报告、收藏和历史快照迁移到另一台电脑？

Local Github Library 把这些操作集中到一个不需要数据库的桌面工具中。业务数据均为普通文本或 ZIP 文件，方便查看、备份和迁移。

## 功能

### 主题查找

- 通过 GitHub Search API 按关键词查找仓库
- 支持 10、20、50、100 及自定义数量，最多 1000 条
- 展示仓库 Star、Fork、Issues、语言、License、Topics、更新时间和描述
- 自动生成包含当前 Star 排行和完整仓库详情的 TXT 报告
- 同一主题至少间隔 7 天后，可使用本地 Star 快照计算增长排行

### 全站总榜

- 支持日、周、月、双月、半年、年六种统计周期
- “Star 增量”使用 OpenGithubs 公开日榜汇总
- “当前 Star”直接读取 GitHub 实时 Star 总量
- 两种榜单完全分开，当前排行不使用统计周期
- 当前总榜使用固定快照切片，切换显示数量不会重新请求并改变前部顺序
- 可查看项目详情、生成功能简介并保存 TXT 报告
- 左侧单独保留历史总榜报告

### 我的收藏

- 新建和重命名收藏目录
- 在收藏页直接搜索 GitHub
- 在“搜索结果”和“目录内容”之间切换
- 收藏单条、多条或全部搜索结果
- 自动跳过同一目录中的重复仓库
- 在目录视图中查看收藏仓库详情
- 双击打开 GitHub
- 从目录中移除误收藏项目

### 仓库功能简介

- 从 GitHub README 自动提取项目概述
- 过滤徽章、图片、代码块、目录、贡献说明和导航链接
- README 不可用时回退到 GitHub Description
- 使用请求取消和内存缓存，快速切换仓库时不会显示过期内容
- 不依赖 OpenAI Key，不产生额外调用费用

### 本地报告库

报告使用可读 TXT 格式，文件名采用：

```text
yyyyMMddHHmmss.txt
```

同一秒发生重名时自动追加序号，不覆盖已有文件。

### 配置导入与导出

- 使用 `.ghconfig` 文件迁移全部数据
- `.ghconfig` 是带版本清单的 ZIP 配置包
- 包含报告、查询记录、收藏、Star 快照和总榜缓存
- 不包含 GitHub Token 和错误日志
- 导入前自动备份当前数据
- 导入后自动重启并加载新数据
- 导入时校验格式、文件数量、体积和路径安全

## 截图

项目截图将在后续版本中补充。

## 系统要求

- Windows 10 或 Windows 11
- 64 位 Intel 或 AMD 处理器
- 可访问 `github.com` 和 `api.github.com`
- 搜索、README 简介和当前总榜需要联网
- 收藏浏览和已下载的总榜缓存可以离线使用

发布版为自包含单文件程序，目标电脑不需要单独安装 .NET 运行时。

## 下载与运行

从 GitHub Releases 下载 `GitHubTopicReports.exe`，放到有写入权限的普通目录后运行。

不要把程序放在需要管理员权限的 `Program Files` 目录中，因为程序需要在 EXE 旁创建：

```text
GitHubReports/
```

## GitHub Token

匿名 GitHub API 可以使用，但请求额度较低。推荐配置个人访问 Token：

1. 在 GitHub 创建只读 Token。
2. 在 EXE 旁创建 `github-token.txt`。
3. 文件内只填写 Token，不要添加其他内容。

也可以使用环境变量：

```powershell
$env:GITHUB_TOKEN = "your_token"
```

请勿把 `github-token.txt` 上传到 GitHub 或发送给其他人。

## 本地数据

程序在 EXE 旁维护以下结构：

```text
GitHubReports/
  Reports/
    20261026143211.txt
  Data/
    搜索记录.txt
    star-snapshots.jsonl
    favorites.json
    global-daily-ranks.zip
    error.log
  Backups/
    before-import-20261026150000.ghconfig
```

说明：

- `Reports`：主题报告、收藏搜索报告和全站总榜报告。
- `搜索记录.txt`：报告时间、关键词和文件名索引。
- `star-snapshots.jsonl`：主题 Star 历史快照，不是数据库。
- `favorites.json`：收藏目录和仓库信息，不是数据库。
- `global-daily-ranks.zip`：全站总榜数据缓存。
- `error.log`：未处理错误诊断记录。
- `Backups`：导入配置前生成的自动备份。

## 数据口径与限制

### 主题增长

GitHub 没有提供“某主题过去 7 天新增 Star”的官方聚合接口。程序会保存同一关键词每次查询时的本地快照，并仅在存在至少 7 天前基线时计算增长。

### 全站增量榜

增量总榜来自 `OpenGithubs/github-daily-rank` 公开日榜。该榜每天列出的是一部分高增长项目，并非 GitHub 官方全量 Star 事件统计。因此：

- 日榜直接使用对应日期的榜单。
- 周榜及更长周期按期间内每日榜单增量累加。
- 未进入某日日榜的日期不会计入该项目。

### 当前 Star 榜

当前榜使用 GitHub Search API 对仓库当前 Star 总量排序。GitHub 全站搜索范围非常大，因此程序会固定分页请求形状，并对当前快照做本地数量切片。

### 功能简介

功能简介是 README 的结构化摘录，不是生成式 AI 的完整翻译或重写。README 为英文时，简介通常仍为英文。

## 开发

### 技术栈

- .NET 10
- WPF
- C#
- GitHub REST API
- System.Text.Json
- System.IO.Compression

### 构建

```powershell
dotnet restore
dotnet build .\GitHubTopicReports.csproj -c Release
```

### 运行

```powershell
dotnet run --project .\GitHubTopicReports.csproj
```

### 发布 Windows x64 单文件

```powershell
.\publish.ps1
```

输出目录：

```text
dist/
```

发布特性：

- 自包含 .NET 运行时
- 单文件 EXE
- 压缩内嵌程序集
- 内嵌 WPF 原生依赖

## 项目结构

```text
.
├─ Models/
│  ├─ ConfigurationPackageResult.cs
│  ├─ FavoriteModels.cs
│  ├─ GlobalLeaderboardModels.cs
│  └─ RepositoryRecord.cs
├─ Services/
│  ├─ AppDiagnostics.cs
│  ├─ ConfigurationPackageService.cs
│  ├─ FavoritesService.cs
│  ├─ GitHubSearchService.cs
│  ├─ GlobalLeaderboardService.cs
│  └─ ReportLibraryService.cs
├─ App.xaml
├─ App.xaml.cs
├─ MainWindow.xaml
├─ MainWindow.xaml.cs
├─ FolderNameDialog.xaml
├─ FolderNameDialog.xaml.cs
├─ GitHubTopicReports.csproj
└─ publish.ps1
```

## 安全与隐私

- GitHub Token 只从环境变量或 EXE 旁的 `github-token.txt` 读取。
- `.ghconfig` 不导出 Token。
- `.ghconfig` 不导出错误日志。
- 导入文件路径经过目录穿越检查。
- 导入前自动生成当前数据备份。
- 程序不会上传本地报告、收藏或 Star 快照到第三方服务。

## 已知限制

- 全站增量榜受 OpenGithubs 日榜覆盖范围影响。
- GitHub Search API 的结果数量上限为 1000 条。
- 匿名 API 的频率限制较低。
- 当前版本未嵌入本地大语言模型，功能简介为 README 摘录。
- 当前只提供 Windows x64 构建。
- 未签名的 EXE 可能触发 Windows SmartScreen 提示。

## 参与贡献

欢迎提交 Issue 和 Pull Request。

提交代码前请确认：

- 不包含 Token、报告或收藏数据。
- 完成 Release 构建。
- 改动了配置格式时同步升级 `formatVersion`。

## 版本

当前首发版本为 `0.1.0`。详细变更见 [CHANGELOG.md](CHANGELOG.md)。

## 致谢

- GitHub REST API
- OpenGithubs 提供的公开开源榜单数据
- .NET 与 WPF

## License

当前仓库尚未指定开源许可证。在添加许可证前，请勿直接将代码作为商业或再分发项目的一部分使用。
