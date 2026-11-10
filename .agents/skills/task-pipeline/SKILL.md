---
name: task-pipeline
description: >-
  基于 Excel 任务清单驱动的开发流水线（任务数据在 docs/tasks/）：读取与筛选任务、
  分析设计拆分、派发执行/审核智能体、构建+测试验证、过程记录回写与按任务提交、
  轻量收尾沉淀记忆。当用户提到 任务清单、跑任务、执行 Excel 任务、批量处理任务、
  任务流水线 时使用，即使没有明说 Excel。仅在本仓库内使用；
  新项目接入见 references/agent-conduct.md 第五节。
---

# 任务流水线（task-pipeline）

## 数据与脚本

- 任务数据：`docs/tasks/tasks.xlsx`（唯一任务源，一任务一行）
- 任务文档：`docs/tasks/records/T########.md`（一任务一文档，承载全过程）
- Excel 读写一律经脚本：`dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs <命令>`（速查见 [references/excel-schema.md](references/excel-schema.md)）
- 智能体规范源在 `.agents/agents/<name>/`（agent.md + config.json）；修改后须跑 `sync-agents.ps1` 并与 `.zcode/agents`、`.github/agents` 三处一起提交

## 流程

| 步 | 动作 | 关键产物 |
|---|---|---|
| 0 | 前置检查 | 工作树干净 + 映射一致 |
| 1 | 读取与筛选 | 本轮任务清单 |
| 2 | 分析设计与拆分 | 子任务 + 任务文档骨架 |
| 3 | 派发 task-executor | 代码变更 + 任务文档 §2/§3/§5 |
| 4 | 派发 task-reviewer | 任务文档 §4 审核小节 |
| 5 | 每任务回写与提交 | Excel 状态回写 + 每任务一个 git 提交 |
| 6 | 轻量收尾 | K 列哈希回填 + 记忆沉淀 + 收尾提交 |

### 第 0 步 前置检查

开工前确认环境，任何一项不满足先处理再继续：

1. `git status` 工作树必须干净（有未提交改动则先请用户处理，不得混入）
2. 确认映射：`.zcode/agents` 与 `.github/agents` 同 `.agents/agents` 一致（不一致先跑 `powershell -ExecutionPolicy Bypass -File .agents/skills/task-pipeline/scripts/sync-agents.ps1`）
3. 必读：`AGENTS.md` → `.agents/memories/project-arch.md` → `.agents/memories/MEMORY.md`（筛相关条目）

### 第 1 步 读取与筛选

从 Excel 取本轮要处理的任务：

- `read`（默认状态=待处理，按优先级 P0→P3）；用户指定类型/编号时加 `--type`/`--id`
- `read --id <编号>` 取完整字段（描述/验收标准原文）供派发与实例化引用
- `read --all` 查看全部状态（含已完成/阻塞的历史）

### 第 2 步 分析设计与拆分

确定做法并建立任务文档，需要时先拆子任务：

- 按任务类型路由 [references/playbooks.md](references/playbooks.md) 对应手册
- 可复用任务定义：`.agents/tasks/<name>/task.md`（如 boundary-tests）——读定义后按其 frontmatter（name/type/priority）与描述/验收模板 `add` 实例化（目标写入 desc 或派发时指定）
- 需要拆分时 `add --parent <编号> …` 生成子任务（编号自动 `T########.n`），并 `log --id <编号> --stage 拆分 --result 成功 --note <拆分说明>`
- 每个任务依次：
  1. `update --id <编号> --status 进行中` + `log --stage 分析`
  2. `doc --id <编号>` 生成任务文档骨架（§1 为一行引用，完整字段在 Excel 行）
  3. 同步任务文档首行状态
- 需求边界不清时在任务文档 §2 列出假设，交执行者确认后再动手

### 第 3 步 执行（派发 task-executor）

把单个任务交给执行智能体实现：

- 逐任务派发 `task-executor` 智能体（本会话未加载该智能体时，由你自己按 playbooks.md 内联执行，并在任务文档注明）
- 派发载荷：`read --id <编号>` 原始输出（完整字段）+ 对应 playbook 类型 + 任务文档路径；执行者开工必读清单见 `.agents/agents/task-executor/agent.md`
- 执行要求（详见 [references/agent-conduct.md](references/agent-conduct.md)）：方案写 §2、时间线实时写 §3、自测后写 §5 草稿
- 执行者不改任务文档 §4；范围外发现记 §5 遗留并反馈
- 执行完成：`log --stage 执行 --result 成功/失败`

### 第 4 步 审核验证（派发 task-reviewer）

独立验证方逐条核对验收标准：

- 派发 `task-reviewer`（或按 [references/review-checklist.md](references/review-checklist.md) 内联执行，注明）
- 审核 = 验收标准逐条核对（证据）+ 代码复查 + **亲自运行**全量构建与测试；结论追加任务文档 §4
- 通过 → 第 5 步；不通过 → 返工（回到第 3 步），`log --stage 返工`
- **返工 ≤2 轮**；第 2 轮仍不通过 → `update --status 阻塞 --result <原因>` + `log --stage 阻塞 --result 失败`
- 审核者不代为修复（保持独立性），返工要求须具体到文件/测试/标准

### 第 5 步 每任务回写与提交

审核通过的任务逐个收口：

- `update --id <编号> --status 已完成 --result <执行+审核一句话结论>`；`log --stage 回写`（关联产物填任务文档路径）
- 提交信息：`<type>: T######## <中文摘要>`（type→前缀映射见 excel-schema.md）
- 每个任务的提交包含：
  - 代码变更
  - `docs/tasks/records/T########.md`
  - `docs/tasks/tasks.xlsx`（状态回写）
- 提交前：全量测试必须绿（命令以 AGENTS.md 为准）；提交后 `log --stage 提交 --ref <short hash>`
- Excel K 列提交哈希**不在此步回填**（避免自引用），统一由第 6 步收尾回填

### 第 6 步 轻量收尾（不生成任何报告文件）

本轮全部任务完成后统一收口：

1. 回填：把本轮各任务提交哈希写入 Excel K 列（`update --commit`）与各任务文档 §5
2. 沉淀记忆：`.agents/memories/lessons.md` 追加 ≥1 条（返工原因/有效做法/验收争议裁决），MEMORY.md 加索引行（条目模板见 agent-conduct.md 六）
3. 核对 `.agents/memories/project-arch.md`：结构有变才更新
4. 收尾提交：`chore: T<首>~T<尾> 收尾回填哈希与记忆`（单任务 `chore: T######## 收尾回填哈希与记忆`），内容：Excel + 各任务文档 §5 哈希 + memories

本步不生成任何报告/总结文件；本轮总结即各任务文档 §5 与 lessons.md。

## 红线

1. 测试不过不得标「已完成」；不得跳过审核
2. 提交前全量测试必须绿（命令以 AGENTS.md 为准）
3. 任务文档时间线必须实时写，事后补写视为未完成
4. Excel 只经脚本修改；阻塞/跳过必须留痕（Log + 结果摘要写原因）
5. 工作树不干净不得开始流水线

## 参考资料（按需读取）

| 文件 | 何时读 |
|---|---|
| [references/excel-schema.md](references/excel-schema.md) | 查 Excel 结构/枚举/编号/脚本用法 |
| [references/playbooks.md](references/playbooks.md) | 执行任务前按类型读对应手册 |
| [references/review-checklist.md](references/review-checklist.md) | 审核前读流程与 §4 格式 |
| [references/agent-conduct.md](references/agent-conduct.md) | 智能体（或内联执行时自己）开工前；含写作模板 |
