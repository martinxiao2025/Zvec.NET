# 项目索引（Zvec.NET）

> 一页纸地图：读完本文件即可定位代码与命令，避免从零探索。结构变化时随任务更新（见文末刷新规则）。

## 技术栈与命令

- 构建：`dotnet build Zvec.NET.slnx`
- 全量测试：`dotnet test Zvec.NET.slnx`（流水线红线：提交前必须全绿）
- 运行示例：`dotnet run --project examples/Zvec.NET.Demo`
- 打 NuGet 包：`dotnet pack src/Zvec.NET`
- 拉取原生 SDK：`powershell -File scripts/fetch-native.ps1`
- 技术栈：.NET 10（`net10.0`）C#；解决方案为新格式 `Zvec.NET.slnx`；版本统一在 `Directory.Build.props`（当前 0.7.0-preview.1）
- 项目本质：阿里巴巴 zvec 嵌入式向量数据库的 .NET 封装（P/Invoke 官方 C API），API 对齐官方 Python SDK v0.7.0；仅支持 win-x64

## 结构表

| 路径 | 职责 | 关键文件 | 测试位置 |
|---|---|---|---|
| `src/Zvec.NET/` | 核心包：P/Invoke 绑定与对象模型 | `Zvec.cs`（入口）、`Collection.*.cs`（集合分部类：Core/Ddl/Fetch/Query/GroupBy/Iterate/Async）、`Model/`、`Rerank/`、`Embedding/` | `tests/Zvec.NET.Tests/` |
| `src/Zvec.NET/Interop/` | 原生互操作层 | `NativeMethods.cs`、`ZvecNative.cs`、`ZvecException.cs`、`ParamBuilder.cs`、`DocCodec.cs`、`NativeTypes.cs`、`NativeUtil.cs` | 同上（`ZvecFixture.cs` 为夹具） |
| `src/Zvec.NET/runtimes/win-x64/native/` | 4 个原生 DLL（勿手动改动） | — | — |
| `src/Zvec.NET.EntityFrameworkCore/` | EF Core 集成包（依赖 EF Core 10.0.0） | `ZvecSet.cs`、`ZvecServiceCollectionExtensions.cs`、`VectorMappingAttributes.cs` | `tests/Zvec.NET.EntityFrameworkCore.Tests/` |
| `examples/Zvec.NET.Demo/` | 控制台示例（EF InMemory） | `Program` | — |
| `tests/Zvec.NET.Tests/` | 核心测试（xunit 2.9.3，真实调用原生库） | `SmokeTests.cs`、`CollectionDmlTests.cs`、`CollectionDdlFtsTests.cs`、`RerankerEmbeddingErrorTests.cs`、`ReviewGapTests.cs`、`ModelBoundaryTests.cs`（SparseVector/Schema 边界） | 自身 |
| `.github/workflows/ci.yml` | CI：windows-latest，build + test | — | — |

## 关键入口

- 用户 API 入口：`src/Zvec.NET/Zvec.cs`
- 原生调用唯一通道：`src/Zvec.NET/Interop/ZvecNative.cs` + `NativeMethods.cs`（内存/句柄安全重点关注区）
- EF 扩展注册：`ZvecServiceCollectionExtensions.cs`
- 测试命名惯例：`SmokeTests` / `CollectionDmlTests` / `ReviewGapTests`（按域分组，见相邻文件）

## 约定速查

- 全项目 `Nullable enable` + `ImplicitUsings enable`；生成 XML 文档注释（CS1591 已抑制）
- `AllowUnsafeBlocks`；核心包 `InternalsVisibleTo` 测试
- 提交惯例：Conventional Commits + 中文描述（见 AGENTS.md）
- 任务流水线：`.agents/skills/task-pipeline/SKILL.md`；任务数据 `tasks/tasks.xlsx`

## 刷新规则

- 新增/移动文件、新增项目 → 更新结构表
- 命令/版本变化 → 更新技术栈与命令
- 每轮任务流水线收尾时核对本文件
