# 智能体行为规范（executor / reviewer 共用）+ 项目接入指南

## 一、开工必读顺序（缺一不可，读完再动手）

1. 项目根 `AGENTS.md` —— 技术栈、构建/测试命令、代码规范、提交惯例
2. `.agents/index.md` —— 项目一页纸地图：结构、关键路径、测试位置
3. `.agents/memory/MEMORY.md` —— 按主题筛相关条目，命中则读 `lessons.md` 对应小节
4. 任务文档 `tasks/records/T########.md` —— 当前任务的全部上下文

**禁止从零探索代码库**：优先用 index.md 的路径直达；索引未覆盖再用搜索。

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
   （复制 `.agents/` 技能+智能体+索引/记忆骨架，生成 `.github/agents/` 映射，创建 `.zcode/agents` junction）
2. **初始化数据**：目标项目内 `dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs init`
3. **补项目约定**：按目标项目实际情况改写 `AGENTS.md` 与 `.agents/index.md`（install 生成的是骨架）
4. **定制范本**：直接编辑 `.agents/skills/task-pipeline/references/*.md`，无需动技能与智能体
5. **验证**：`read` 输出正常；新会话能发现 task-pipeline 技能与两个智能体

### AGENTS.md 骨架要点（install 生成后必改）

- 构建/测试/运行命令（占位 `<BUILD_CMD>` 等，必须替换为真实命令）
- 代码规范（nullable/命名/注释要求）
- 提交信息惯例（本流水线默认 Conventional Commits + 中文描述）
- 「clone 后先跑 sync-agents.ps1」提示
