# LLM 模型库

这里是 Local GitHub Library 的可选本地模型下载页。模型与程序版本分开发布，升级程序时不需要重新下载模型。

## 推荐模型

| 模型 | 大小 | 最低程序版本 | License |
| --- | ---: | --- | --- |
| [Qwen2.5 1.5B Instruct Q4_K_M](https://github.com/huaaorain/local-github-library/releases/download/models-qwen2.5-1.5b-v1/Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm) | 1.04 GB | 0.2.0 | Apache-2.0 |

校验文件：[SHA256SUMS.txt](https://github.com/huaaorain/local-github-library/releases/download/models-qwen2.5-1.5b-v1/SHA256SUMS.txt)

```text
7c3ef213cb5fb12bb33591f8d6a09e1da9c187704bfdec8e11ffd1487a83c629  Qwen2.5-1.5B-Instruct-Q4_K_M.ghllm
```

## 安装

1. 下载 `.ghllm` 文件。
2. 打开 Local GitHub Library，点击顶部的“导入 LLM”。
3. 选择下载的模型包并等待校验完成。
4. 仓库详情中的“AI 总结与翻译”会使用该模型；未安装模型时，其他功能不受影响。

模型只用于本地推理，不需要 OpenAI API Key。推荐至少 6–8 GB 可用内存。

## 发布规则

- 模型发布使用独立的 `models-*` 标签，并标记为 GitHub Pre-release。
- 普通程序版本只包含程序包、EXE 和发布校验文件，不再包含 `.ghllm` 或“完整离线包”。
- 只有模型文件本身更新时才创建新模型发布，例如 `models-qwen2.5-1.5b-v2`。
- 旧模型发布保持可下载，以便旧版程序继续使用。

## Model downloads

The local LLM is optional and versioned separately from the application. Download the `.ghllm` package above once, then import it with **导入 LLM**. Application upgrades do not require downloading the model again.
