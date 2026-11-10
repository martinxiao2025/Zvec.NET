# 经验沉淀（最新在最上；每条：情境/决策或踩坑/结论；流水线每轮至少 1 条）

## 2026-11-10 PowerShell 脚本编码
- **情境**：sync-agents.ps1 含中文，Windows PowerShell 5.1 以 `-File` 运行时报「字符串缺少终止符」。
- **踩坑**：Write 工具默认输出 UTF-8 无 BOM；PowerShell 5.1 对无 BOM 脚本按 ANSI 解析，中文字符串损坏导致语法错误。
- **结论**：本仓库所有 `.ps1` 必须保存为 UTF-8 **带 BOM**；转换命令 `[IO.File]::WriteAllText($p, [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8), (New-Object Text.UTF8Encoding($true)))`。
