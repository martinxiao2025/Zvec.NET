# Zvec.NET — .NET 10 封装库实施计划

## 背景与可行性（调研已完成）

Zvec 是阿里巴巴开源（Apache 2.0）的**进程内嵌入式向量数据库**（C++ 核心，无服务器进程）。官方有 Python/Node/Go/Rust/Dart/C/C++ SDK，无 .NET SDK。

**可行路径已确认**：
- 官方 C API：`src/include/zvec/c_api.h`（v0.7.0，约 4500 行）。不透明句柄（`zvec_collection_t` 等）、`zvec_error_code_t` 错误码 + `zvec_get_last_error_details()` 错误详情、显式 create/destroy 内存约定、ptr+length 字符串/向量传递 —— P/Invoke 友好。
- 预编译原生库：GitHub Releases v0.7.0 的 `zvec-sdk-windows-amd64.zip`（约 22MB），内含 C API 动态库，无需编译 C++。
- Python 公开 API（Collection/Doc/Schema/Params/Config）与 C API 一一对应，C# 封装可完整对齐。

## 已确认的决策

| 决策项 | 选择 |
|---|---|
| 包结构 | 单包 `Zvec`（绑定 + win-x64 原生库一体） |
| 平台 | 仅 win-x64（后续版本再扩展） |
| API 风格 | 同步核心 API + `Task.Run` 薄包装 async API |
| 扩展层 | 本地 Reranker + HTTP embedding（OpenAI/Qwen/Jina 兼容）+ BM25 |

## 项目结构

```
D:\WorkSpace\Zvec.NET\
├── Zvec.sln
├── src/Zvec/                          # NuGet 包 Zvec (net10.0)
│   ├── Interop/
│   │   ├── NativeTypes.cs             # zvec_error_code_t、zvec_string_view_t、zvec_float_array_t 等结构体/枚举映射
│   │   ├── Native.cs                  # LibraryImport (source generator) 绑定，按 init/config、schema、index、collection、doc、query、辅助类型分组
│   │   ├── SafeHandles.cs             # CollectionHandle、IndexParamsHandle、FieldSchemaHandle、ZvecStringHandle 等句柄
│   │   └── ZvecException.cs           # 错误码→异常，message 取自 zvec_get_last_error_details
│   ├── Model/
│   │   ├── Doc.cs                     # Id、Score、Vectors(dict)、Fields(dict)
│   │   ├── Schema/                    # CollectionSchema、FieldSchema、VectorSchema + DataType 枚举
│   │   └── Params/                    # Hnsw/HnswRabitq/Ivf/IvfRabitq/DiskAnn/Flat/Invert/Fts 索引参数；各 QueryParam；Query、Fts、CollectionOption 等
│   ├── Collection.cs                  # 对照 Python Collection 全部方法
│   ├── Zvec.cs                        # 静态入口：Init/CreateAndOpen/Open/Shutdown
│   ├── Rerank/                        # IReRanker、RrfReRanker、WeightedReRanker、CallbackReRanker
│   ├── Embedding/                     # IEmbeddingFunction、OpenAI/Qwen/Jina 客户端、BM25
│   └── runtimes/win-x64/native/       # 从官方 SDK zip 提取的 C API dll
├── tests/Zvec.Tests/                  # xUnit，真实调用原生库的 round-trip 测试
├── examples/Zvec.Demo/                # 控制台示例（对照 Python quickstart）
├── scripts/fetch-native.ps1           # 下载 v0.7.0 windows SDK → 提取头文件与 dll
└── README.md
```

## 实施步骤

1. **环境与素材准备**：确认本机 .NET 10 SDK；运行 `scripts/fetch-native.ps1` 下载 `zvec-sdk-windows-amd64.zip`，提取 dll 到 `runtimes/win-x64/native/`、头文件到 `src/Zvec/Interop/c_api.h`（实现期唯一事实源）；用 `dumpbin /exports`（或 PowerShell 反射）核对导出符号。
2. **脚手架**：sln + 三个项目（Zvec 类库 + xUnit 测试 + Demo），配置 csproj（`net10.0`、`AllowUnsafeBlocks`、原生库随包打包、NuGet 元数据 Apache-2.0 注记）。
3. **Interop 层**：按 c_api.h 逐组编写 `NativeTypes` → `SafeHandles` → `Native` 绑定与错误转换辅助方法（`ThrowIfError`）。
4. **Model 层**：Schema/参数/Doc 及枚举，命名对齐 Python（HnswIndexParam、MetricType 等）。
5. **核心门面（同步）**：`Zvec.Init/Open/CreateAndOpen` + `Collection` 全方法（insert/upsert/update/delete/deleteByFilter/fetch/iterDocs/query/groupByQuery/createIndex/dropIndex/optimize/addColumn/dropColumn/alterColumn/flush/close/destroy）。
6. **async 包装**：每个公共同步方法加 `Async` 后缀版本（`Task.Run` 薄包装）；`iterDocs` 提供 `IEnumerable<Doc>` 与 `IAsyncEnumerable<Doc>`。
7. **Reranker**：RRF / Weighted / Callback 三个本地实现，接口对齐 Python `rerank(query_results, topn, fields)`。
8. **Embedding**：共享 `EmbeddingHttpClient`；OpenAI/Qwen/Jina 兼容客户端（dense + sparse 变体，api_key/timeout/dimension）；本地 BM25（zh/en）。**安全约束**：仅允许 http/https；发请求前校验 host，拒绝 localhost、环回、私有和保留地址（不留 localhost 默认 base_url）。
9. **测试**：临时目录建库 round-trip（建 schema→insert→query→fetch→filter→FTS→delete→destroy）、错误路径（重复 init、非法参数）、reranker 单测、embedding 客户端 host 校验单测（mock handler）。
10. **收尾**：Demo（对照 Python quickstart 场景）、README（安装/快速上手/API 对照表/平台说明）、`dotnet pack` 验证包内含原生库。

## 关键技术约定

- P/Invoke 用 `LibraryImport` source generator（AOT 友好）；字符串一律 UTF-8；向量传 `float[]`/`ReadOnlySpan<float>`。
- 所有非零 `zvec_error_code_t` → 抛 `ZvecException`（含 code + 原生 message/file/line）。
- 原生库解析顺序：默认 `runtimes/win-x64/native` 自动探测 → 允许 `ZvecNative.TrySetLibraryPath(path)` 手动指定（`NativeLibrary.Load`）。
- API 对齐 v0.7.0；Zvec 处于 0.x 快速演进期，公共类型标注 `RequiresUnreferencedCode` 无关项与 `Experimental` 视情况，README 注明对齐版本。

## 风险

- c_api.h 的 collection/doc/query 段（后 2/3）的具体签名在实现时逐条对照，若个别 Python 能力（如 BM25 corpus 统计）未在 C API 暴露，对应功能降级或标注 limited，并在 README 说明。
- win-x64 dll 约 22MB，包体积可控；若 NuGet 上 `Zvec` 包 ID 被占用则改用 `Zvec.NET`。