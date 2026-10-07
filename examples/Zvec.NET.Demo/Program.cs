using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Zvec.NET;
using Zvec.NET.Embedding;
using Zvec.NET.EntityFrameworkCore;
using static Zvec.NET.Zvec;

// ============================================================================
// Zvec.NET Demo —— 对照官方 Python quickstart 的完整场景
// ============================================================================

// 1. 初始化运行时（进程内一次；日志级别等可选）
Init(new ZvecOptions { LogLevel = LogLevel.Warn });
Console.WriteLine($"Zvec 原生库版本: {NativeVersion}");

string dbPath = Path.Combine(Path.GetTempPath(), "zvec-net-demo-" + Guid.NewGuid().ToString("N")[..8]);

// 2. 定义 Schema：标量字段 + 向量字段
var schema = new CollectionSchema("demo_collection")
    .AddField(new FieldSchema("title", DataType.String))
    .AddField(new FieldSchema("category", DataType.String))
    .AddField(new FieldSchema("age", DataType.Int32))
    .AddVector(new VectorSchema("embedding", DataType.VectorFp32, dimension: 4));

using Collection collection = CreateAndOpen(dbPath, schema);

// 3. 写入文档（稠密向量）
float[][] vectors =
[
    [0.9f, 0.1f, 0.0f, 0.0f],
    [0.1f, 0.9f, 0.1f, 0.0f],
    [0.0f, 0.1f, 0.9f, 0.1f],
    [0.9f, 0.0f, 0.1f, 0.1f],
];
string[] titles = ["机器学习入门", "深度学习实战", "向量数据库", "搜索引擎原理"];

var docs = new List<Doc>();
for (int i = 0; i < titles.Length; i++)
{
    docs.Add(new Doc($"doc{i}",
        fields: new() { ["title"] = titles[i], ["category"] = i % 2 == 0 ? "ml" : "search", ["age"] = 20 + i },
        vectors: new() { ["embedding"] = vectors[i] }));
}

foreach (WriteResult result in collection.Insert(docs))
{
    Console.WriteLine($"插入 {result}");
}

// 4. 建索引（可选；未建索引时引擎自动暴力检索）
collection.CreateIndex("embedding", new HnswIndexParam(MetricType.Cosine, m: 8, efConstruction: 64));
Console.WriteLine($"Stats: 文档数={collection.Stats.DocCount}, 索引=[{string.Join(", ", collection.Stats.Indexes.Select(i => $"{i.Name}:{i.Completeness:P0}"))}]");

// 5. 向量检索
Console.WriteLine("\n-- 向量检索（最接近第 1 篇） --");
foreach (Doc doc in collection.Query(new Query("embedding", vector: vectors[0]), topk: 3))
{
    Console.WriteLine($"  {doc.Id}  score={doc.Score:F4}  title={doc.Field("title")}");
}

// 6. 带过滤的检索
Console.WriteLine("\n-- 向量检索 + 过滤 (category = 'search') --");
foreach (Doc doc in collection.Query(new Query("embedding", vector: vectors[0]), topk: 3, filter: "category = 'search'"))
{
    Console.WriteLine($"  {doc.Id}  title={doc.Field("title")}  category={doc.Field("category")}");
}

// 7. Fetch 按主键取回
Console.WriteLine("\n-- Fetch --");
Doc? fetched = collection.Fetch("doc2").GetValueOrDefault("doc2");
Console.WriteLine($"  doc2 -> title={fetched?.Field("title")}, 向量维度={((float[]?)fetched?.Vector("embedding"))?.Length}");

// 8. 稀疏向量 + 混合检索
var sparseSchema = new CollectionSchema("sparse_demo")
    .AddField(new FieldSchema("text", DataType.String))
    .AddVector(new VectorSchema("dense", DataType.VectorFp32, 4))
    .AddVector(new VectorSchema("sparse", DataType.SparseVectorFp32, 0));

using Collection sparseCollection = CreateAndOpen(dbPath + "-sparse", sparseSchema);
sparseCollection.Insert([
    new Doc("s1", fields: new() { ["text"] = "vector database" },
        vectors: new() { ["dense"] = vectors[0], ["sparse"] = new SparseVector([1u, 5u], [0.6f, 0.8f]) }),
    new Doc("s2", fields: new() { ["text"] = "full text search" },
        vectors: new() { ["dense"] = vectors[2], ["sparse"] = new SparseVector([2u, 5u], [0.5f, 0.9f]) }),
]);

Console.WriteLine("\n-- 混合检索（稠密 + 稀疏两路 RRF） --");
IReadOnlyList<Doc> hybrid = sparseCollection.Query(
[
    new Query("dense", vector: vectors[0]),
    new Query("sparse", vector: new SparseVector([1u], [1.0f])),
], topk: 2, reranker: new RrfReRanker());
foreach (Doc doc in hybrid)
{
    Console.WriteLine($"  {doc.Id}  score={doc.Score:F4}  text={doc.Field("text")}");
}

// 9. 本地 BM25 稀疏嵌入（corpus 训练）
Console.WriteLine("\n-- BM25 本地稀疏嵌入 --");
var bm25 = new BM25Embedding(["向量数据库检索", "全文搜索引擎", "机器学习模型"], encodingType: "query");
SparseVector sv = bm25.Embed("向量检索");
Console.WriteLine($"  \"向量检索\" -> {sv.Count} 个非零项: [{string.Join(", ", sv.Indices.Zip(sv.Values, (i, v) => $"{i}:{v:F3}"))}]");

// 10. 本地 Ollama 嵌入（bge-m3）端到端语义检索
Console.WriteLine("\n-- 本地 Ollama（bge-m3）语义检索 --");
OllamaDemo.Run(dbPath + "-ollama");

// 11. 全量迭代
Console.WriteLine("\n-- 迭代全部文档 --");
foreach (Doc doc in collection.IterateDocs(outputFields: ["title"]))
{
    Console.WriteLine($"  {doc.Id}: {doc.Field("title")}");
}

// 12. EF Core 集成：实体注解映射 + 混合检索闭环
Console.WriteLine("\n-- EF Core 集成 (Zvec.NET.EntityFrameworkCore) --");
EfDemo.Run(dbPath + "-ef");

// 13. 清理
collection.Destroy();
sparseCollection.Destroy();
Console.WriteLine("\n完成（集合已从磁盘删除）。");

/// <summary>本地 Ollama 演示：bge-m3 真实嵌入 → Zvec 写入 → 语义检索（含过滤）。
/// 依赖 http://localhost:11434 与 bge-m3 模型；不可用时打印提示并跳过。</summary>
internal static class OllamaDemo
{
    private const string BaseUrl = "http://localhost:11434";

    public static void Run(string path)
    {
        if (!IsBgeM3Available())
        {
            Console.WriteLine("  跳过：未检测到本地 Ollama 或 bge-m3 模型（可运行: ollama pull bge-m3）。");
            return;
        }

        // OpenAI 兼容端点；本地端点需显式 allowLocalEndpoint: true（默认安全策略拒绝内网地址）。
        using var embedder = new OpenAIEmbedding(
            model: "bge-m3", baseUrl: BaseUrl + "/v1",
            allowLocalEndpoint: true, timeout: TimeSpan.FromMinutes(2));

        string[] texts =
        [
            "机器学习模型的训练需要大量标注数据",
            "深度神经网络通过反向传播优化参数",
            "向量数据库支持高维嵌入的相似度检索",
            "语义搜索把文本编码为向量再计算距离",
            "今晚的晚餐是一碗热腾腾的牛肉面",
        ];
        string[] categories = ["ml", "ml", "vecdb", "vecdb", "food"];

        float[][] vectors = embedder.EmbedBatch(texts);
        Console.WriteLine($"  bge-m3 嵌入维度: {vectors[0].Length}");

        var schema = new CollectionSchema("ollama_demo")
            .AddField(new FieldSchema("text", DataType.String))
            .AddField(new FieldSchema("category", DataType.String))
            .AddVector(new VectorSchema("embedding", DataType.VectorFp32, (uint)vectors[0].Length));
        using Collection collection = CreateAndOpen(path, schema);

        collection.Insert(texts.Select((text, i) => new Doc($"ollama{i}",
            fields: new() { ["text"] = text, ["category"] = categories[i] },
            vectors: new() { ["embedding"] = vectors[i] })));
        collection.Flush();

        float[] query = embedder.Embed("如何用向量数据库做语义检索");

        Console.WriteLine("  语义检索: \"如何用向量数据库做语义检索\"");
        foreach (Doc doc in collection.Query(new Query("embedding", vector: query), topk: 3))
        {
            Console.WriteLine($"    {doc.Id}  score={doc.Score:F4}  [{doc.Field("category")}] {doc.Field("text")}");
        }

        Console.WriteLine("  同一检索 + 过滤 (category = 'ml')：");
        foreach (Doc doc in collection.Query(new Query("embedding", vector: query), topk: 3, filter: "category = 'ml'"))
        {
            Console.WriteLine($"    {doc.Id}  score={doc.Score:F4}  [{doc.Field("category")}] {doc.Field("text")}");
        }

        collection.Destroy();
    }

    /// <summary>探测本地 Ollama 与 bge-m3 是否可用（3 秒超时，失败一律视为不可用）。</summary>
    private static bool IsBgeM3Available()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using JsonDocument doc = JsonDocument.Parse(
                client.GetStringAsync(BaseUrl + "/api/tags").GetAwaiter().GetResult());
            foreach (JsonElement model in doc.RootElement.GetProperty("models").EnumerateArray())
            {
                if (model.GetProperty("name").GetString()?.StartsWith("bge-m3", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}

/// <summary>EF 集成演示：业务实体在 EF，向量在 Zvec，检索后回查实体。</summary>
internal static class EfDemo
{
    [VectorCollection("demo_products")]
    public sealed class Product
    {
        [VectorKey]
        public string Sku { get; set; } = "";

        public string Name { get; set; } = "";

        public double Price { get; set; }

        [VectorField(Dimension = 4)]
        public float[]? Embedding { get; set; }
    }

    private sealed class ShopContext : DbContext
    {
        public DbSet<Product> Products => Set<Product>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseInMemoryDatabase("demo-shop");

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<Product>().HasKey(p => p.Sku);
    }

    public static void Run(string path)
    {
        using ZvecSet<Product> vectorSet = ZvecSet<Product>.Create(path,
            options => options.WithVectorIndex(p => p.Embedding, new HnswIndexParam(MetricType.Cosine, 8, 64)));

        using var context = new ShopContext();
        var products = new List<Product>
        {
            new() { Sku = "sku-1", Name = "机械键盘", Price = 399, Embedding = [0.9f, 0.1f, 0, 0] },
            new() { Sku = "sku-2", Name = "无线鼠标", Price = 199, Embedding = [0.1f, 0.9f, 0.1f, 0] },
        };
        context.Products.AddRange(products);
        context.SaveChanges();

        // 实体 → 向量集合同步
        vectorSet.UpsertRange(products);

        // 向量检索拿键与得分 → EF 回查实体（混合检索闭环）
        List<SearchHit<Product>> similar = vectorSet
            .FindSimilarAsync(context, [0.9f, 0.1f, 0, 0], topk: 2)
            .GetAwaiter().GetResult();
        foreach (SearchHit<Product> hit in similar)
        {
            Console.WriteLine($"  {hit.Entity.Sku}  score={hit.Score:F4}  {hit.Entity.Name}（实体来自 EF）");
        }

        vectorSet.Underlying.Destroy();
    }
}
