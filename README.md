# Zvec.NET

[![License: Apache-2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](https://www.apache.org/licenses/LICENSE-2.0)

**Zvec.NET** 是 [Zvec](https://zvec.org) —— 阿里巴巴开源的进程内嵌入式向量数据库 —— 的 .NET 10 封装库，通过 P/Invoke 直接调用官方 C API（`zvec_c_api`），API 面对齐官方 [Python SDK](https://zvec.org/api-reference/python/)。

- 无服务器、无守护进程：检索直接嵌入应用进程
- 稠密 / 稀疏 / BM25 全文 / 混合检索
- HNSW / IVF / IVF-RaBitQ / DiskANN / Vamana / Flat / Invert / FTS 索引
- 标量过滤、分组、多路重排（RRF / Weighted / Callback）
- 同步 API + `Task.Run` 异步包装 + `IAsyncEnumerable` 迭代
- OpenAI / Qwen / Jina 兼容 HTTP 嵌入客户端 + 本地 BM25 稀疏嵌入

> 对齐原生版本 **v0.7.0**（win-x64）。Zvec 处于 0.x 快速演进期，升级原生版本时 API 可能变化。

> **异步取消提示**：`Collection` / `ZvecSet` 的 `*Async` 方法均为线程池 `Task.Run` 薄包装，用于脱离调用方同步上下文、支持并发吞吐；`CancellationToken` 仅在任务启动前生效，一旦原生阻塞（P/Invoke）调用开始执行便无法中断。需要硬超时请在托管侧配合 `CancellationTokenSource.CancelAfter` 或任务等待超时实现。

## 安装

```
dotnet add package Zvec.NET                          # 核心：绑定 + win-x64 原生库
dotnet add package Zvec.NET.EntityFrameworkCore      # 可选：EF Core 实体向量集集成
```

核心包自带 win-x64 原生库（`runtimes/win-x64/native/` 下 `zvec_c_api.dll` 及其依赖），安装即用。如需自定义原生库路径，可随时调用 `ZvecNative.TrySetLibraryPath(path)`（追加搜索目录，立即生效）。

## 快速上手

```csharp
using Zvec.NET;
using static Zvec.NET.Zvec;   // Init / Open / CreateAndOpen 入口

Init();                        // 进程内一次；可选 new ZvecOptions { ... }

var schema = new CollectionSchema("my_collection")
    .AddField(new FieldSchema("title", DataType.String))
    .AddField(new FieldSchema("age", DataType.Int32))
    .AddVector(new VectorSchema("embedding", DataType.VectorFp32, dimension: 4));

using Collection collection = CreateAndOpen("./my_collection", schema);

collection.Insert([
    new Doc("doc1",
        fields:   new() { ["title"] = "hello", ["age"] = 1 },
        vectors:  new() { ["embedding"] = new float[] { 0.9f, 0.1f, 0, 0 } }),
    new Doc("doc2",
        fields:   new() { ["title"] = "world", ["age"] = 2 },
        vectors:  new() { ["embedding"] = new float[] { 0.1f, 0.9f, 0, 0 } }),
]);

// 向量检索（未建索引时自动暴力检索；可建 HNSW 加速）
IReadOnlyList<Doc> results = collection.Query(
    new Query("embedding", vector: new float[] { 0.9f, 0.1f, 0, 0 }),
    topk: 2,
    filter: "age > 0");

foreach (Doc doc in results)
    Console.WriteLine($"{doc.Id} score={doc.Score} title={doc.Field("title")}");
```

### 混合检索（稠密 + 稀疏 + RRF）

```csharp
collection.Query(
[
    new Query("dense",  vector: new float[] { 0.9f, 0.1f }),
    new Query("sparse", vector: new SparseVector([1u, 5u], [0.6f, 0.8f])),
], topk: 10, reranker: new RrfReRanker());
```

### 全文检索（BM25）

```csharp
var schema = new CollectionSchema("fts_demo")
    .AddField(new FieldSchema("content", DataType.String,
        indexParam: new FtsIndexParam("standard")))
    .AddVector(new VectorSchema("embedding", DataType.VectorFp32, 4));

collection.Query(new Query("content", fts: new Fts(matchString: "vector search")));
```

### 嵌入客户端

```csharp
// OpenAI 兼容端点（含 Qwen 兼容模式、Jina）
using var embedder = new OpenAIEmbedding(
    model: "text-embedding-3-small",
    apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
float[] vector = embedder.Embed("hello world");

// 本地 BM25（corpus 训练，无需网络）
var bm25 = new BM25Embedding(["语料一", "语料二"], encodingType: "query");
SparseVector sparse = bm25.Embed("查询");
```

> **安全策略**：HTTP 嵌入客户端仅允许 http/https，且默认拒绝 localhost、环回、私有、链路本地、CGNAT（100.64/10）与保留地址（含 IPv4-mapped / NAT64 / 6to4 / Teredo / `::` 等 IPv4 承载与未指定形式，防 SSRF）。自建客户端不使用系统代理、不自动跟随重定向（3xx 视为错误）——若部署环境需要经代理出网，请传入自定义 `HttpClient` 并自行确保其安全策略。**本地部署场景**（Ollama、vLLM、LM Studio 等）可显式传入 `allowLocalEndpoint: true` 放行，此时协议限制仍然生效：
>
> ```csharp
> var embedder = new OpenAIEmbedding(
>     model: "bge-m3",
>     baseUrl: "http://localhost:11434/v1",   // Ollama 的 OpenAI 兼容端点
>     allowLocalEndpoint: true);
> ```

## EF Core 集成（Zvec.NET.EntityFrameworkCore）

Zvec 是嵌入式向量库而非关系库，本包采用业界标准的「**EF 实体 ↔ 向量集合**」集成模式：实体按注解映射为 Zvec schema，Upsert 同步业务数据，相似检索由向量集合产出键与得分后**回查 EF 实体**（混合检索闭环）。

```csharp
using Zvec.NET.EntityFrameworkCore;

[VectorCollection("products")]
public class Product
{
    [VectorKey]                        // zvec 主键（string/int/long/Guid）
    public string Sku { get; set; }

    public string Name { get; set; }   // 标量字段自动映射
    public double Price { get; set; }

    [VectorField(Dimension = 4)]       // 稠密向量 FP32
    public float[]? Embedding { get; set; }

    [VectorIgnored]                    // 不参与同步
    public byte[]? Blob { get; set; }
}

// 创建集合并同步实体
using ZvecSet<Product> productVectors = ZvecSet<Product>.Create(
    "./vec-products",
    options => options.WithVectorIndex(p => p.Embedding, new HnswIndexParam(MetricType.Cosine)));

productVectors.UpsertRange(products);

// 纯向量检索：键 + 得分 + 字段投影
IReadOnlyList<SearchHit> hits = productVectors.Search(vector, topk: 5, filter: "Price < 100");

// 混合检索闭环：回查 EF 实体
List<SearchHit<Product>> similar =
    await productVectors.FindSimilarAsync(dbContext, vector, topk: 5);
foreach (var hit in similar)
    Console.WriteLine($"{hit.Entity.Name} score={hit.Score}");
```

DI（ASP.NET Core / Generic Host）：

```csharp
builder.Services.AddZvecSets(
    configureEngine: o => o.LogLevel = LogLevel.Error,
    configureSets: sets => sets
        .AddSet<Product>("./vec-products", o => o.WithVectorIndex(p => p.Embedding, new HnswIndexParam(MetricType.Cosine)))
        .AddExistingSet<Article>("./vec-articles"));

// 注入 IZvecSet<Product> 使用
```

说明与约定：
- 键：`[VectorKey]` 或约定 `Id`/`<实体名>Id` 属性；非 string 键以字符串形式存储并在 EF 查询中经 `ToString()` 对齐（生产关系库上建议直接用 string 键）。
- 向量：`[VectorField(Dimension = n)]` 的 `float[]`（FP32）或 `SparseVector` 属性；默认不建索引（引擎暴力检索），用 `WithVectorIndex` 配置 HNSW 等。
- 底层 `Underlying`（`Collection`）可使用全部原生能力（Fetch/Iterate/DDL/统计等）。


## Python → C# API 对照

| Python | Zvec.NET |
| --- | --- |
| `zvec.init(...)` | `Zvec.Init(ZvecOptions?)` |
| `zvec.create_and_open(path, schema, option)` | `Zvec.CreateAndOpen(path, schema, option?)` |
| `zvec.open(path, option)` | `Zvec.Open(path, option?)` |
| `Collection.insert / upsert / update(docs)` | `Collection.Insert / Upsert / Update(Doc / IEnumerable<Doc>)` → `WriteResult([])` |
| `Collection.delete(ids) / delete_by_filter(filter)` | `Collection.Delete(...) / DeleteByFilter(filter)` |
| `Collection.fetch(ids, output_fields, include_vector)` | `Collection.Fetch(...)` → `Dictionary<string, Doc>` |
| `Collection.query(queries, topk, filter, ...)` | `Collection.Query(Query / IReadOnlyList<Query>, ...)` |
| `Collection.group_by_query(...)` | `Collection.GroupByQuery(...)`（客户端模拟，见下） |
| `Collection.iter_docs(...)` | `Collection.IterateDocs()` / `IterateDocsAsync()` |
| `Collection.create_index / drop_index / optimize` | `Collection.CreateIndex / DropIndex / Optimize` |
| `Collection.add_column / drop_column / alter_column` | `Collection.AddColumn / DropColumn / AlterColumn` |
| `HnswIndexParam / IVFIndexParam / FlatIndexParam / ...` | 同名类 |
| `RrfReRanker / WeightedReRanker / CallbackReRanker` | `RrfReRanker / WeightedReRanker / CallbackReRanker`（`IReRanker`） |
| `OpenAIDenseEmbedding / QwenDenseEmbedding / JinaDenseEmbedding` | `OpenAIEmbedding / QwenDenseEmbedding / JinaEmbedding` |
| `BM25EmbeddingFunction` | `BM25Embedding`（仅 corpus 训练路径） |

## 与 Python SDK 的已知差异（v0.7.0 C API 能力边界）

- **稀疏向量单路查询**：C API 单路查询无法表达稀疏向量，绑定层自动复制为两路相同子查询并用 RRF 合并（结果等价）。
- **GroupByQuery**：C API 未暴露引擎内 group-by 执行入口，当前为客户端模拟（放大 topk 后分组截断），超大规模下与引擎语义可能有差异。
- **HnswRabitqIndexParam**：C API 仅暴露 metric/quantize；`m`/`ef_construction`/`total_bits` 等使用引擎默认值。
- **OptimizeOption / IndexOption 的 concurrency**：C API 未暴露线程数参数。
- **ARRAY_BOOL**：支持写入；读回暂不支持（C API 位打包格式丢失长度信息）。
- **ARRAY_STRING**：引擎读回时会丢弃空串元素（`["a", "", "b"]` 读回为 `["a", "b"]`），属 C API 序列化行为。
- **空字段投影**：`outputFields` 传 null 表示全部标量字段；空列表一律拒绝（C API 在不同路径上把空投影分别解释为"全部字段"与"不取字段"，语义矛盾）。
- **单路重排**：`reranker` 仅对多路（≥2 路）检索生效；单路/纯过滤查询传入非 null reranker 会抛 `ArgumentException`。
- **BM25**：不依赖 dashtext 预训练编码器，仅支持用户提供 corpus 的本地训练路径；分词为 CJK 单字 + 拉丁词元。
- **过滤表达式**：`filter` / 回填 `expression` 由引擎解析，属非参数化接口；来源不可信时调用方须自行校验（绑定层拒绝空串与 NUL 字节）。

## 平台支持

| RID | 状态 |
| --- | --- |
| win-x64 | ✅ 随包分发 |
| linux-x64 / linux-arm64 / osx-arm64 等 | 官方 SDK 有产物，计划后续版本打包 |

## 项目结构

```
src/Zvec.NET/                       # 核心包：P/Invoke 绑定（对照 c_api.h v0.7.0）+ 原生库
├── Interop/                        #   绑定与编解码
├── Model/                          #   Schema / IndexParams / QueryParams / Doc / 枚举
├── Rerank/                         #   RRF / Weighted / Callback 重排
├── Embedding/                      #   HTTP 嵌入客户端 + 本地 BM25
└── runtimes/win-x64/native/
src/Zvec.NET.EntityFrameworkCore/  # EF Core 集成包：实体注解映射 + ZvecSet<TEntity> + 混合检索
examples/Zvec.NET.Demo/             # 完整示例（含本地 Ollama bge-m3 语义检索，未安装时自动跳过）
tests/                              # 测试（核心 123 项 + EF 集成 16 项，真实调用原生库）
scripts/fetch-native.ps1            # 从 GitHub Releases 拉取原生 SDK
```

## 开发

```
dotnet test               # 运行全部测试（Ollama E2E: dotnet test --filter Category=RequiresOllama）
dotnet run --project examples/Zvec.NET.Demo
dotnet pack src/Zvec.NET  # 打 NuGet 包
```

## 许可

- 本封装库：Apache-2.0
- [Zvec](https://github.com/alibaba/zvec) 原生库：Apache-2.0（阿里巴巴）
