# AGENTS.md — Zvec.NET 项目约定

## 构建与测试

- 构建：`dotnet build Zvec.NET.slnx`
- 全量测试：`dotnet test Zvec.NET.slnx`（任务流水线红线：每次提交前必须全绿）
- 运行示例：`dotnet run --project examples/Zvec.NET.Demo`
- 打包：`dotnet pack src/Zvec.NET`
- 拉取原生 SDK：`powershell -File scripts/fetch-native.ps1`

## 代码规范

- 全项目 `Nullable enable` + `ImplicitUsings enable`；公共 API 生成 XML 文档注释
- 核心包 `AllowUnsafeBlocks`，P/Invoke 边界（`Interop/`）注意内存与句柄释放
- 测试框架 xunit；新测试放在 `tests/Zvec.NET.Tests/`（或 EF 测试项目），命名与相邻测试文件风格一致
- 仅支持 win-x64；原生 DLL 位于 `src/Zvec.NET/runtimes/win-x64/native/`，勿手动改动

## 提交惯例

- Conventional Commits + 中文描述（如 `fix: 修复并发安全问题`）
- 任务流水线的提交格式：`<type>: T######## <中文摘要>`（type 映射见 excel-schema.md）

## 任务流水线

- 智能体映射已随仓库分发：`.zcode/agents/`（ZCode 副本）与 `.github/agents/`（Copilot 映射）clone 即用，无需重建
- 修改 `.agents/agents/` 后须执行 `powershell -ExecutionPolicy Bypass -File .agents/skills/task-pipeline/scripts/sync-agents.ps1`，并将规范源与两处副本三处一起提交
- 初始化任务清单：`dotnet run .agents/skills/task-pipeline/scripts/taskexcel.cs init`
- 完整流程见 `.agents/skills/task-pipeline/SKILL.md`；项目地图见 `.agents/index.md`；历史经验见 `.agents/memory/`
