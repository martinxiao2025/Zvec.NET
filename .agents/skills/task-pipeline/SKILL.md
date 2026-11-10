---
name: task-pipeline
version: 1.0.0
description: >-
  基于 Excel 任务清单驱动的开发流水线：读取 tasks/tasks.xlsx，按类型和状态分析设计、
  拆分子任务，派发执行/审核智能体，构建+测试验证，过程记录回写，汇总生成含任务编号的
  统一执行报告，并按任务类型做 Conventional Commits 提交。当用户提到 任务清单、跑任务、
  执行 Excel 任务、任务报告、批量处理任务、任务流水线 时使用，即使没有明说 Excel。
  仅在本仓库内使用；新项目接入见 references/agent-conduct.md 第五节。
---

# 任务流水线（task-pipeline）

Excel（`tasks/tasks.xlsx`）为唯一任务源；每任务一份文档（`tasks/records/T########.md`）承载全过程；报告按编号范围命名；每任务一个 git 提交。

## 脚本

所有 Excel 读写经 `dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs <命令>`（速查见 [references/excel-schema.md](references/excel-schema.md)）。

## 流程

### 第 0 步 前置检查

1. `git status` 工作树必须干净（有未提交改动则先请用户处理，不得混入）
2. 确认映射：`.zcode/agents` 存在且 `.github/agents/*.agent.md` 与 `.agents/agents/*.md` 同步（不一致先跑 `powershell -File .agents/skills/task-pipeline/scripts/sync-agents.ps1`）
3. 必读：`AGENTS.md` → `.agents/index.md` → `.agents/memory/MEMORY.md`（筛相关）

### 第 1 步 读取与筛选

- `read`（默认状态=待处理，按优先级 P0→P3）；用户指定类型/编号时加 `--type`/`--id`
- `read --id <编号>` 取完整字段供派发

### 第 2 步 分析设计与拆分

- 按任务类型路由 [references/playbooks.md](references/playbooks.md) 对应手册
- 需要拆分时 `add --parent <编号> …` 生成子任务（编号自动 `T########.n`），并 `log --id <编号> --stage 拆分 --result 成功 --note <拆分说明>`
- 每个任务：`update --id <编号> --status 进行中` + `log --stage 分析`；`doc --id <编号>` 生成任务文档骨架；同步任务文档首行状态

### 第 3 步 执行（派发 task-executor）

- 逐任务派发 `task-executor` 智能体（本会话未加载该智能体时，由你自己按 playbooks.md 内联执行，并在报告中注明）
- 派发输入：任务完整字段（read --id 输出）+ 对应 playbook 类型 + 任务文档路径
- 执行要求（详见 [references/agent-conduct.md](references/agent-conduct.md)）：先读 AGENTS/index/memory，方案写 §2、时间线实时写 §3、自测后写 §5 草稿
- 执行完成：`log --stage 执行 --result 成功/失败`

### 第 4 步 审核验证（派发 task-reviewer）

- 派发 `task-reviewer`（或按 [references/review-checklist.md](references/review-checklist.md) 内联执行，注明）
- 审核 = 验收标准逐条核对（证据）+ 代码复查 + **亲自运行**全量构建与测试；结论追加任务文档 §4
- 通过 → `update --status 待审核`→`待审核`后按第 5 步收尾；不通过 → 返工（回到第 3 步），`log --stage 返工`
- **返工 ≤2 轮**；第 2 轮仍不通过 → `update --status 阻塞 --result <原因>` + `log --stage 阻塞 --result 失败`

### 第 5 步 状态回写

- `update --id <编号> --status 已完成 --result <执行+审核一句话结论>`
- `log --stage 回写`（关联产物填任务文档路径）

### 第 6 步 git 提交（每任务一个）

- 提交信息：`<前缀>: T######## <中文摘要>`，前缀取自类型映射（excel-schema.md）
- 每个任务的提交包含：代码变更 + `tasks/records/T########.md` + `tasks/tasks.xlsx`（状态回写）
- 提交前：全量测试必须绿；`log --stage 提交 --ref <short hash>`
- Excel K 列提交哈希**不在此步回填**（避免自引用），统一由第 7 步报告提交回填

### 第 7 步 汇总报告与收尾闭环

1. `stats` → 按 [references/templates.md](references/templates.md) 报告模板生成
   `docs/reports/<日期>-<最小编号>~<最大编号>-report.md`（单任务 `-T########-`）
2. 回填：把本轮各任务提交哈希写入 Excel K 列（`update --commit`）与各任务文档 §5
3. 沉淀记忆：`.agents/memory/lessons.md` 追加 ≥1 条（返工原因/有效做法/验收争议裁决），MEMORY.md 加索引行
4. 核对 `.agents/index.md`：结构有变则更新
5. 最终提交：`docs: T<最小编号>~T<最大编号> 生成 <日期> 任务执行报告并回填提交哈希`（报告 + Excel + memory + index）

## 红线

1. 测试不过不得标「已完成」；不得跳过审核
2. 提交前全量测试必须绿（命令以 AGENTS.md 为准）
3. 任务文档缺失或时间线事后补写 → 视为未完成
4. Excel 只经脚本修改；阻塞/跳过必须留痕（Log + 结果摘要写原因）
5. 工作树不干净不得开始流水线

## 参考资料（按需读取）

| 文件 | 何时读 |
|---|---|
| [references/excel-schema.md](references/excel-schema.md) | 查 Excel 结构/枚举/编号/脚本用法 |
| [references/playbooks.md](references/playbooks.md) | 执行任务前按类型读对应手册 |
| [references/review-checklist.md](references/review-checklist.md) | 审核前读流程与 §4 格式 |
| [references/templates.md](references/templates.md) | 写任务文档/报告/记忆条目时 |
| [references/agent-conduct.md](references/agent-conduct.md) | 智能体（或内联执行时自己）开工前 |
