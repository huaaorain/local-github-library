# Local Github Library 0.2.0

这是 Local Github Library 的第二个公开版本，重点加入可选的本地 LLM、
独立提示词管理和更可靠的 README 摘要处理。

## 相对 0.1.0 的主要更新

### 本地 LLM 与提示词

- 新增可选 `.ghllm` 本地模型包，无需 OpenAI API Key 或云端推理服务。
- 集成 llama.cpp CPU 推理运行时，支持 Windows x64。
- 推荐模型为 Qwen2.5 1.5B Instruct Q4_K_M。
- 在仓库详情中流式生成中文翻译和中文总结。
- 新增居中的 LLM 设置窗口，可分别管理模型包和提示词。
- 提示词保存为独立 TXT 文件，可导入、编辑、保存和导出。
- 支持 `{{FullName}}`、`{{Description}}`、`{{Topics}}` 和
  `{{ReadmeSummary}}` 占位符。
- 提示词内容进入缓存键，切换提示词后不会复用旧结果。
- 修复单文件发布版缺少 LLamaSharp CPU 原生库的问题。
- 修复模型复述提示词、输出格式说明的问题。
- 未安装模型包时继续使用 README 摘要，基础功能不受影响。

### README 摘要

- 过滤多语言切换栏、双语导航和纯分隔符导航。
- 过滤 README 顶部的 IMPORTANT、WARNING 和 CAUTION 提示块。
- 继续过滤徽章、图片、代码块、链接导航和贡献说明。
- README 不可用时回退到 GitHub Description。

### 收藏体验

- “查找项目”和“我的收藏”统一使用同一份收藏目录数据。
- 修复“查找项目”目标目录下拉框为空的问题。
- 当前目录已有仓库时禁用单条收藏按钮。
- 所选项目或全部搜索结果均已收藏时禁用批量收藏按钮。
- 混合选择时只添加缺失仓库，并显示跳过数量。
- 收藏下拉框文字在垂直方向居中。

### 发布与构建

- `publish.ps1` 自动发布 LLamaSharp CPU 原生运行库。
- 发布脚本发现还原或发布失败时立即停止并返回错误。
- GitHub Actions 上传完整 Windows 发布目录。

## Release 资产

- `LocalGithubLibrary-0.2.0-win-x64.zip`
  - 推荐下载，包含程序、原生运行库和当前提示词。
- `LocalGithubLibrary-0.2.0-full-win-x64.zip`
  - 完整离线包，额外包含已安装的 Qwen2.5 1.5B 模型和提示词。
- `Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm`
  - 可单独导入的本地 LLM 模型包。
- `LocalGithubLibrary-0.2.0-source.zip`
  - 对应标签 `v0.2.0` 的源码快照。
- `GitHubTopicReports.exe`
  - 单独主程序，适合已有 `runtimes` 目录的升级。
- `SHA256SUMS.txt`
  - 所有二进制资产及模型的校验值。

## 运行说明

- Windows 10/11 x64。
- 基础程序不要求预先安装 .NET。
- 完整离线包已包含模型，可直接使用本地 LLM。
- 单独导入模型时，通过右上角“导入 LLM”选择 `.ghllm` 文件。
- 必须保留 EXE 同级的 `runtimes` 目录。
- 模型包采用 Qwen2.5 官方 Apache-2.0 许可。

## 已知限制

- 只提供 Windows x64 构建。
- 当前 Star 总榜和仓库搜索受 GitHub API 额度影响。
- 全站 Star 增量榜依赖 OpenGithubs 的公开日榜覆盖范围。
- 本地 1.5B 模型适合摘要与翻译，不应视为事实核查工具。
