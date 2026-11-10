# install-to.ps1 — 将任务流水线安装到其他项目（复用入口）
# 复制 .agents 技能+智能体，生成目标项目的索引/记忆/AGENTS.md 骨架，并执行映射同步
# 用法: powershell -File <本项目>/.agents/skills/task-pipeline/scripts/install-to.ps1 -Target <目标项目根>
param(
    [Parameter(Mandatory = $true)][string]$Target,
    [switch]$Force   # 覆盖目标已有的同名骨架文件（index.md / memory / AGENTS.md）
)

$ErrorActionPreference = 'Stop'
$srcRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$dstRoot = if (Test-Path $Target) { (Resolve-Path $Target).Path } else { New-Item -ItemType Directory -Path $Target -Force | Out-Null; (Resolve-Path $Target).Path }
if ($srcRoot -eq $dstRoot) { Write-Error '目标项目不能是源项目自身'; exit 1 }
Write-Host "安装任务流水线: $srcRoot -> $dstRoot"

# 1) 复制技能与智能体
foreach ($pair in @(
        @('skills\task-pipeline', '.agents\skills\task-pipeline'),
        @('agents', '.agents\agents'))) {
    $from = Join-Path (Join-Path $srcRoot '.agents') $pair[0]
    $to = Join-Path $dstRoot $pair[1]
    New-Item -ItemType Directory -Force -Path (Split-Path $to -Parent) | Out-Null
    if (Test-Path $to) { Remove-Item -Recurse -Force $to }
    Copy-Item -Recurse -Force $from $to
    Write-Host "[OK] 复制 $($pair[0])"
}

New-Item -ItemType Directory -Force -Path (Join-Path $dstRoot '.agents\memory') | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
function Put-File([string]$path, [string]$content) {
    if ((Test-Path $path) -and -not $Force) { Write-Host "[=] 已存在，跳过 $path"; return }
    [System.IO.File]::WriteAllText($path, $content, $utf8)
    Write-Host "[OK] 写入 $path"
}

# 2) 索引骨架
$indexTmpl = @'
# 项目索引（骨架 — 请按目标项目实际情况改写）

> 一页纸地图：读完本文件即可定位代码与命令，避免从零探索。结构变化时随任务更新。

## 技术栈与命令
- 构建：`<BUILD_CMD>`
- 全量测试：`<TEST_CMD>`（流水线红线：提交前必须全绿）
- 运行示例：`<RUN_CMD>`
- 技术栈：`<语言/框架/版本>`

## 结构表
| 路径 | 职责 | 关键文件 | 测试位置 |
|---|---|---|---|
| `<src/...>` | <职责> | <关键文件> | <tests/...> |

## 关键入口
- <核心类型/入口：路径>

## 约定速查
- 提交惯例：Conventional Commits + 中文描述（见 AGENTS.md）
- 任务流水线：`.agents/skills/task-pipeline/SKILL.md`

## 刷新规则
- 新增/移动文件、新增项目：更新结构表
- 命令变化：更新命令区
'@
Put-File (Join-Path $dstRoot '.agents\index.md') $indexTmpl

# 3) 记忆骨架
$memoryTmpl = @'
# 记忆索引（最新在最上；一行一条，指向 lessons.md）

<!-- 格式：- yyyy-MM-dd <主题>（T########）→ lessons.md#锚点 -->
'@
Put-File (Join-Path $dstRoot '.agents\memory\MEMORY.md') $memoryTmpl
$lessonsTmpl = @'
# 经验沉淀（最新在最上；每条：情境/决策或踩坑/结论；流水线每轮至少 1 条）

'@
Put-File (Join-Path $dstRoot '.agents\memory\lessons.md') $lessonsTmpl

# 4) AGENTS.md 骨架（不覆盖已有）
$agentsTmpl = @'
# AGENTS.md（骨架 — 必须替换占位符为真实命令与规范）

## 构建与测试
- 构建：`<BUILD_CMD>`
- 全量测试：`<TEST_CMD>`
- 运行示例：`<RUN_CMD>`

## 代码规范
- <nullable/命名/注释/框架约定>

## 提交惯例
- Conventional Commits + 中文描述；任务提交格式 `<type>: T######## <摘要>`

## 任务流水线
- 智能体映射已随仓库分发：`.zcode/agents/`（ZCode 副本）与 `.github/agents/`（Copilot 映射）clone 即用，无需重建
- 修改 `.agents/agents/` 后须执行 `powershell -File .agents/skills/task-pipeline/scripts/sync-agents.ps1`，并将三处一起提交
- 初始化任务清单：`dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs init`
- 详见 `.agents/skills/task-pipeline/SKILL.md` 与 `.agents/index.md`
'@
Put-File (Join-Path $dstRoot 'AGENTS.md') $agentsTmpl

# 5) 目标项目内执行映射同步（.zcode/agents 副本 + .github/agents 映射）
$sync = Join-Path $dstRoot '.agents\skills\task-pipeline\scripts\sync-agents.ps1'
& powershell -ExecutionPolicy Bypass -File $sync
Write-Host '完成。后续步骤：1) 改写 AGENTS.md 与 .agents/index.md 2) 按需定制 references/ 3) taskexcel.cs init 建任务清单'
