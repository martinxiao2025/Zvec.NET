# 智能体行为规范（executor / reviewer 共用）+ 项目接入指南 + 写作模板

## 一、开工必读顺序（只读以下清单，勿全量读 references/）

**executor（执行者）**：

1. 项目根 `AGENTS.md` —— 技术栈、构建/测试命令、代码规范、提交惯例
2. `.agents/memories/project-arch.md` —— 项目一页纸地图：结构、关键路径、测试位置
3. `.agents/memories/MEMORY.md` —— 按主题筛相关条目，命中则读 `lessons.md` 对应小节（未命中不读 lessons.md）
4. 任务文档 `docs/tasks/records/T########.md` —— 当前任务的全部上下文
5. 本文件（纪律与完成标准）+ `playbooks.md` 中本任务类型对应的那一节

**reviewer（审核者）**：

1. 项目根 `AGENTS.md`
2. 任务文档全文（§1 验收标准是核对基准）
3. `review-checklist.md`（审核流程与 §4 格式）+ 本文件「二、纪律」一节

**禁止从零探索代码库**：优先用 project-arch.md 的路径直达；索引未覆盖再用搜索。

## 二、纪律

1. **边做边写**：任务文档 §3 时间线在执行中实时追加；禁止完成后凭记忆补写
2. **证据文化**：一切结论（完成/通过/✅）必须有证据——文件:行、测试名、命令输出行
3. **如实报告**：测试失败就写失败；无法复现就写无法复现；禁止美化和臆造
4. **范围纪律**：只做本任务的事；发现范围外问题记入任务文档 §5 遗留并反馈编排者，不顺手扩大改动
5. **角色边界**：执行者不改 §4；审核者不改 §2/§3/§5，只在 §4 追加；审核者不代为修复
6. **命令以 AGENTS.md 为准**：不得自行发明构建/测试命令

## 三、执行者（task-executor）完成标准

- 按类型 playbook（playbooks.md）完成，§2/§3 填写完整，§5 结论草稿就绪
- 构建 + 相关测试 + 全量测试全部通过（输出关键行写入 §3/§5）
- 交付时向编排者报告：变更文件清单、测试结果、遗留问题

## 四、审核者（task-reviewer）完成标准

- 按 review-checklist.md 流程与 §4 格式追加审核小节
- 亲自运行全量构建与测试；判定（通过/不通过）+ 返工要求（若不通过）

## 五、新项目接入指南（复用本流水线）

1. **复制**：在装有本流水线的项目里执行
   `powershell -File .agents/skills/task-pipeline/scripts/install-to.ps1 -Target <目标项目根>`
   （复制 `.agents/` 技能+目录化智能体（含 config.json）+可复用任务定义+三占位文件，生成 `.zcode/agents/` 副本与 `.github/agents/` Copilot 映射，并为目标 `.gitignore` 写入 `.zcode` 放行规则）
2. **初始化数据**：目标项目内 `dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs init`（数据在 `docs/tasks/`）
3. **补项目约定**：按目标项目实际情况改写 `AGENTS.md` 与 `.agents/memories/project-arch.md`（install 生成的是骨架）
4. **定制范本**：直接编辑 `.agents/skills/task-pipeline/references/*.md`，无需动技能与智能体
5. **验证**：`read` 输出正常；新会话能发现 task-pipeline 技能与两个智能体

### AGENTS.md 骨架要点（install 生成后必改）

- 构建/测试/运行命令（占位 `<BUILD_CMD>` 等，必须替换为真实命令）
- 代码规范（nullable/命名/注释要求）
- 提交信息惯例（本流水线默认 Conventional Commits + 中文描述）
- 「智能体副本随仓库分发；修改 `.agents/agents` 后须跑 sync-agents.ps1 并三处一起提交」提示

## 六、写作规范（模板唯一来源）

### 1. 任务文档模板（docs/tasks/records/T########.md）

骨架由 `doc --id` 生成；以下为各节填写要求：

```markdown
# T20260001 <标题>

> 状态：<状态> ｜ 类型：<类型> ｜ 优先级：<P0-P3> ｜ 执行者：<执行者> ｜ 创建：<yyyy-MM-dd HH:mm>

## 1. 任务内容
> 见 Excel 行（`read --id <编号>` 获取完整字段）。类型：<type> ｜ 优先级：<P> ｜ 状态随首行同步。

## 2. 分析与设计
（执行者。方案要点；涉及模块与文件清单；子任务拆分及理由；对公共 API 的影响；假设与边界）

## 3. 执行过程
（执行者。执行中实时追加，禁止事后补写）
| 时间 | 动作 | 详情 |
|-------|------|------|
| 11:20 | 定位 | …（文件:行） |
| 11:35 | 修改 | …（做了什么、为什么） |
| 11:50 | 自测 | <命令> → <结果> |

## 4. 审核记录
（审核者只追加、不修改上文；每轮一个小节，格式见 review-checklist.md）

## 5. 结论
（结果摘要 ≤200 字；提交哈希；遗留问题与后续建议）
```

状态每次变更（update --status）后，编排者同步更新首行 `> 状态：…`，保持 Excel 与文档一致。

### 2. 记忆条目模板（.agents/memories/lessons.md）

按时间**倒序**追加（最新在最上）；每条三行体，编号关联任务：

```markdown
## yyyy-MM-dd T######## <主题≤20字>
- **情境**：遇到了什么/要决定什么
- **决策/踩坑**：做了什么选择或错在哪
- **结论**：下次遇到该怎么做的可执行一句话
```

MEMORY.md 索引同步加一行（最新在最上）：

```markdown
- yyyy-MM-dd <主题>（T########）→ lessons.md#yyyy-mm-dd-txxxxxxxx-主题
```
