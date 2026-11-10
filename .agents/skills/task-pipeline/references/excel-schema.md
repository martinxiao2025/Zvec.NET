# Excel 规范（tasks/tasks.xlsx）

任务流水线的唯一持久化数据源。由 `scripts/taskexcel.cs` 读写，任何修改状态/记录的动作都必须经过脚本完成，禁止手工编辑破坏结构。

## 文件与路径约定

| 对象 | 路径（相对项目根，根 = 向上找到 .git 的目录） |
|---|---|
| 任务清单 | `tasks/tasks.xlsx` |
| 任务文档 | `tasks/records/T########.md`（一任务一文档） |
| 执行报告 | `docs/reports/YYYY-MM-DD-Txxxxxxx~Tyyyyyyy-report.md` |

## Sheet「Tasks」列定义

| 列 | 列名 | 校验 | 必填 | 说明 |
|---|---|---|---|---|
| A | 编号 | `T\d{8}(\.\d+)?` 唯一 | ✓ | 脚本分配，见下方编号规则 |
| B | 类型 | 下拉：feature/bug/test/docs/refactor/chore | ✓ | 决定执行 playbook 与提交前缀 |
| C | 状态 | 下拉：待处理/进行中/待审核/已完成/已跳过/阻塞 | ✓ | 状态机见下 |
| D | 优先级 | 下拉：P0/P1/P2/P3 | ✓ | 读取时按 P0→P3 排序 |
| E | 标题 | ≤50 字 | ✓ | 一句话概括 |
| F | 描述 | 文本 | ✓ | 需求/背景/bug 复现步骤 |
| G | 验收标准 | 多条以 `；` 分隔 | ✓ | 审核逐条核对的依据 |
| H | 关联编号 | 父任务编号 | – | 子任务填父编号 |
| I | 执行者 | task-executor / 人名 | – | |
| J | 结果摘要 | ≤200 字 | – | 执行+审核一句话结论 |
| K | 提交哈希 | short hash | – | 报告提交时统一回填，日常不填 |
| L | 更新时间 | `yyyy-MM-dd HH:mm` | 自动 | update 时脚本写入 |

## 状态机

```
待处理 →(分析拆分)→ 进行中 →(执行完成)→ 待审核 →(审核通过)→ 已完成
                        ↑                      │
                        └──(不通过，≤2 轮返工)──┘
待审核 →(返工超 2 轮)→ 阻塞
任何状态 →(须在结果摘要写原因)→ 已跳过
```

## 编号规则（8 位数字，前 4 位年份）

- 格式：`T` + 年份 4 位 + 年内流水 4 位，如 `T20260001`；子任务 `T20260001.1`（最多一级嵌套）
- 分配：仅由 `add` 命令分配——取当前年份的年内最大流水 +1；跨年从 `0001` 重新起号
- 只增不复用：跳过/删除的任务编号永久保留
- 派生键：Excel 两表、任务文档文件名、报告文件名与小节标题、git 提交信息，全部使用全编号

## Sheet「Log」列定义（追加型事件日志，只增不改）

| 列 | 列名 | 说明 |
|---|---|---|
| A | 记录编号 | `R####`，脚本自增 |
| B | 任务编号 | 对应 Tasks 表 A 列 |
| C | 时间 | `yyyy-MM-dd HH:mm` |
| D | 阶段 | 下拉：分析/拆分/执行/审核/返工/回写/提交/阻塞/跳过 |
| E | 执行者 | 智能体名或人名 |
| F | 结果 | 下拉：成功/失败/通过/不通过/— |
| G | 说明 | 一句话详情，详细过程指向任务文档对应小节 |
| H | 关联产物 | 任务文档小节锚点/文件路径/提交哈希 |

## Sheet「Dict」

枚举与规则的单一来源：类型/状态/优先级/阶段/结果五组枚举、状态机说明、type→提交前缀映射。Tasks/Log 的下拉校验引用本表区域；修改枚举只改这里（同时需同步修改 taskexcel.cs 中的常量）。

## type → git 提交前缀映射

| 类型 | 前缀 | | 类型 | 前缀 |
|---|---|---|---|---|
| feature | feat | | docs | docs |
| bug | fix | | refactor | refactor |
| test | test | | chore | chore |

## 脚本速查

```
dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs init [--sample] [--force]
dotnet run ... read [--status X|--type Y|--id Z|--all]     # 默认状态=待处理，按优先级排序
dotnet run ... add --type T --title X --desc Y --accept Z [--priority P] [--parent ID] [--executor E]
dotnet run ... update --id X [--status S] [--result R] [--executor E] [--commit H]
dotnet run ... log --id X --stage S --result R --note N [--ref W] [--executor E]
dotnet run ... stats
dotnet run ... doc --id X                                    # 生成任务文档骨架（不覆盖已有）
```
