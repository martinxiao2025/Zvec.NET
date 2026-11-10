# 经验沉淀（最新在最上；每条：情境/决策或踩坑/结论；流水线每轮至少 1 条）

## 2026-11-10 findstr 搜中文不可信（编码伪象）
- **情境**：复审者用 `findstr /C:"报告"` 检查 SKILL.md 得「零命中」，与首审（Read 工具）发现的 2 处矛盾。
- **踩坑**：CMD 下 findstr 模式串按 GBK 编码、文件是 UTF-8——字节不匹配导致漏检（假阴性）；与 dotnet test 管道乱码同根源。
- **结论**：搜仓库文件的中文一律用 PowerShell 显式 UTF-8（`[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8)`）或支持 UTF-8 的工具；findstr 只用于 ASCII 模式。

## 2026-11-10 PS5.1 强转 List 是定长集合
- **情境**：install-to.ps1 用 `[List[string]](Get-Content $gi)` 后 `RemoveAt` 抛「集合的大小是固定的」。
- **踩坑**：PS5.1 集合强转 `[List[T]]` 得定长包装，不支持增删。
- **结论**：可变列表用 `New-Object System.Collections.Generic.List[string]` 再 `AddRange`，不要强转。

## 2026-11-10 CMD 中分号不是命令分隔符
- **情境**：执行者在 CMD 里用 `;` 串联多条命令，`;` 后内容被并入 dotnet 参数导致 MSB1008 构建错误。
- **踩坑**：CMD 的 `;` 是参数分隔符（同空格），不是 PowerShell/ bash 的语句分隔符。
- **结论**：本仓库 shell 为 CMD：多命令一律 `&&` 串联或拆成多次调用；PowerShell 片段用 `powershell -Command "..."` 包裹。

## 2026-11-10 智能体映射用入库副本不用 junction
- **情境**：T20260004 前用 junction（.zcode/agents → .agents/agents），git 无法表达，clone 后必须重建，切换 IDE 体验差。
- **决策**：junction 退役，改为三处真实副本——`.agents/agents/`（规范源）+ `.zcode/agents/`（原样复制）+ `.github/agents/*.agent.md`（Copilot 转换），均入库；`.gitignore` 用 `.zcode/*` + `!.zcode/agents/` 精确放行，防本地工具状态误入库。
- **结论**：改智能体只改 `.agents/agents/`，跑 `sync-agents.ps1`（幂等），三处一起提交；任何项目 clone 即用，无需生成。

## 2026-11-10 spreadsheets 面板读不了 tasks.xlsx
- **情境**：用 ZCode spreadsheets 面板（open_document）抽查 tasks/tasks.xlsx 报 `corrupt_file: Cannot read properties of undefined (reading 'sheets')`。
- **踩坑**：面板内置解析器无法解析 ClosedXML 0.105.1 生成的 xlsx（文件本身有效：ClosedXML 反复读写、数据校验均正常，Excel 可正常打开）。
- **结论**：抽查 tasks.xlsx 一律用 `dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs read [--id X]`；不要依赖 spreadsheets 面板，也无需"修复"文件。

## 2026-11-10 时间戳必须取系统时间
- **情境**：首跑时任务文档 §3/§4 的时间戳凭感觉手工估算，收尾比对 Excel Log 表（脚本写入的真实时间）发现整体偏快约 30 分钟，被迫回头修正三份文档。
- **踩坑**：手工估算的时间无法回溯，破坏「时间线=证据」的可信度。
- **结论**：任务文档时间线一律以 `taskexcel.cs log`（脚本取系统时钟）的记录为准；写 §3 时直接引用 Log 表时间，禁止估写。

## 2026-11-10 任务流水线首跑
- **情境**：流水线首次端到端运行（T20260001~T20260003，docs/test/chore 各一）。
- **决策/踩坑**：① 提交事件 Log 必须在 git commit 之后记，导致该条日志只能随下一次提交入库——按任务提交时事件流会「滞后一条」；② 中文标题的 GitHub 锚点需按「小写、去标点、空格转连字符」生成，v0.7.0 会变 v070。
- **结论**：跑流水线时按「分析→执行→审核→回写→提交→(提交事件 Log)」顺序操作即可，最后一条日志滞后属正常现象，报告提交统一收口；写 README 锚点链接时用上述规则手工推导即可（本轮 5 个锚点全部有效）。

## 2026-11-10 dotnet test 输出编码
- **情境**：CMD 下 `dotnet test ... | findstr "已通过"` 匹配不到任何行，退出码 1。
- **踩坑**：中文版 dotnet test 输出经管道/重定向后按 GBK 字节写出，findstr 的 UTF-8 模式串匹配失败；直接管道还会因 findstr 无命中返回非零。
- **结论**：验证测试结果用「重定向到临时文件 + PowerShell Get-Content 读尾部」或直接信任退出码（0=全绿），不要在 CMD 管道里 grep 中文。

## 2026-11-10 PowerShell 脚本编码
- **情境**：sync-agents.ps1 含中文，Windows PowerShell 5.1 以 `-File` 运行时报「字符串缺少终止符」。
- **踩坑**：Write 工具默认输出 UTF-8 无 BOM；PowerShell 5.1 对无 BOM 脚本按 ANSI 解析，中文字符串损坏导致语法错误。
- **结论**：本仓库所有 `.ps1` 必须保存为 UTF-8 **带 BOM**；转换命令 `[IO.File]::WriteAllText($p, [IO.File]::ReadAllText($p, [Text.Encoding]::UTF8), (New-Object Text.UTF8Encoding($true)))`。
