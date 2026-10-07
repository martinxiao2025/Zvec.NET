using System.Net;
using System.Text;
using Xunit;
using Zvec.NET.Embedding;

namespace Zvec.NET.Tests;

/// <summary>第五轮审查（2026-10-07）修复项的回归测试。</summary>
public sealed class Review5RegressionTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public Review5RegressionTests(ZvecFixture fixture) => _fixture = fixture;

    private Collection CreateStringArrayCollection() =>
        Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5arr")
            .AddField(new FieldSchema("tags", DataType.ArrayString, nullable: true)));

    // P0：ARRAY_STRING 拼接总字节数为 8 倍数时曾触发引擎 zvec_string_t** 启发式误判（野指针崩溃）。
    [Theory]
    [InlineData(new string[] { "machine" }, 8)]                       // 单串恰 8 字节
    [InlineData(new string[] { "a", "b", "c", "d" }, 8)]              // 多短串合计 8 字节
    [InlineData(new string[] { "机器学习", "vector", "db" }, 0)]       // 多字节 UTF-8 混合
    [InlineData(new string[] { "0123456789abcdef" }, 17)]             // 17 字节（非 8 倍数对照）
    [InlineData(new string[] { }, 0)]                                 // 空数组
    public void ArrayStringRoundTrips(string[] tags, int _)
    {
        using Collection collection = CreateStringArrayCollection();
        Assert.True(collection.Upsert(new Doc("d1", fields: new() { ["tags"] = tags })).Success);

        Dictionary<string, Doc> fetched = collection.Fetch("d1", includeVector: false);
        Assert.Equal(tags, (string[])fetched["d1"].Field("tags")!);
    }

    // P1：零命中查询曾因 Marshal.Copy 空指针抛 ArgumentNullException。
    [Fact]
    public void ZeroHitQueryReturnsEmptyResult()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5empty")
            .AddField(new FieldSchema("age", DataType.Int32)));

        // 空集合 + 无过滤条件的纯查询。
        Assert.Empty(collection.Query(topk: 5));

        collection.Insert(new Doc("a", fields: new() { ["age"] = 10 }));
        // 过滤条件永不命中的查询。
        Assert.Empty(collection.Query(topk: 5, filter: "age > 1000"));
    }

    // P1：Delete 主键 null/空串曾以 NULL char* 直达引擎。
    [Fact]
    public void DeleteRejectsNullAndEmptyIds()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5del")
            .AddField(new FieldSchema("t", DataType.String)));

        string nullId = null!;
        string emptyId = "";
        Assert.Throws<ArgumentException>(() => collection.Delete([nullId, "ok"]));
        Assert.Throws<ArgumentException>(() => collection.Delete([emptyId]));
        Assert.Throws<ArgumentException>(() => collection.Delete(nullId));
    }

    // P1：空投影列表在引擎各路径语义互相矛盾（单路=全部字段、多路=无字段），统一拒绝。
    [Fact]
    public void EmptyOutputFieldsRejectedOnAllPaths()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5proj")
            .AddField(new FieldSchema("t", DataType.String)));

        Assert.Throws<ArgumentException>(() => collection.Fetch(["a"], outputFields: []));
        Assert.Throws<ArgumentException>(() => collection.Query(topk: 1, outputFields: []));
        Assert.Throws<ArgumentException>(() => collection.IterateDocs(outputFields: []).ToList());
        // null（全部字段）与含 null 元素的列表行为不变/被拒绝。
        Assert.Throws<ArgumentException>(() => collection.Query(topk: 1, outputFields: [null!]));
    }

    [Fact]
    public void NonPositiveTopkRejected()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5topk")
            .AddField(new FieldSchema("t", DataType.String)));

        Assert.Throws<ArgumentOutOfRangeException>(() => collection.Query(topk: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => collection.Query(topk: -3));
    }

    [Fact]
    public void NullQueryListThrowsArgumentNull()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5nullq")
            .AddField(new FieldSchema("t", DataType.String)));

        System.Collections.Generic.IReadOnlyList<Query> nullQueries = null!;
        Assert.Throws<ArgumentNullException>(() => collection.Query(nullQueries));
    }

    [Fact]
    public void InsertNullDocThrowsArgumentNull()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5nulldoc")
            .AddField(new FieldSchema("t", DataType.String)));

        Assert.Throws<ArgumentNullException>(() => collection.Insert([new Doc("ok"), null!]));
    }

    [Fact]
    public void GroupByRejectsNonPositiveParameters()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5grp")
            .AddField(new FieldSchema("g", DataType.String)));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => collection.GroupByQuery(new Query("v"), "g", groupCount: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => collection.GroupByQuery(new Query("v"), "g", topkPerGroup: 0));
    }

    // P2：AddColumn 曾静默丢弃 FieldSchema.IndexParam（引擎限制：add_column 带索引时仅支持数值列）。
    [Fact]
    public void AddColumnAppliesIndexParam()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5ddl")
            .AddField(new FieldSchema("t", DataType.String)));

        collection.AddColumn(new FieldSchema("score", DataType.Int32, nullable: true, indexParam: new InvertIndexParam()));
        FieldSchema? added = collection.Schema.Field("score");
        Assert.NotNull(added);
        Assert.Equal(IndexType.Invert, added.IndexParam?.Type);
    }

    [Fact]
    public void DdlRejectsInvalidNames()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("rv5ddl2")
            .AddField(new FieldSchema("t", DataType.String)));

        Assert.Throws<ArgumentException>(() => collection.AddColumn(new FieldSchema("", DataType.Int32)));
        Assert.Throws<ArgumentException>(() => collection.AlterColumn("t", newName: "bad\0name"));
    }

    // P2：NaN 与任何区间比较恒为 false，曾可直达引擎配置。
    [Fact]
    public void NaNRatioRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Zvec.Init(new ZvecOptions { BruteForceByKeysRatio = float.NaN }));
    }

    // P2：BM25 语料 null 元素曾抛 NRE。
    [Fact]
    public void BM25NullCorpusElementRejected()
    {
        Assert.Throws<ArgumentException>(() => new BM25Embedding(["ok", null!]));
    }

    // 空路径在托管层拒绝（此前依赖引擎报错）。
    [Fact]
    public void EmptyPathRejectedAtManagedLayer()
    {
        string emptyPath = "";
        Assert.Throws<ArgumentException>(() => Zvec.Open(emptyPath));
        Assert.Throws<ArgumentException>(() => Zvec.CreateAndOpen(emptyPath, new CollectionSchema("x")
            .AddField(new FieldSchema("t", DataType.String))));
    }
}

/// <summary>第五轮嵌入客户端修复（BOM 容忍、错误体前缀读取、Qwen/Jina 异步）的 mock 测试。</summary>
public sealed class Review5EmbeddingMockTests : IDisposable
{
    private readonly StubHandler _handler = new(_ => Json(HttpStatusCode.OK, Encoding.UTF8.GetBytes("{}")));
    private readonly HttpClient _httpClient;

    public Review5EmbeddingMockTests() =>
        _httpClient = new HttpClient(_handler);

    public void Dispose() => _httpClient.Dispose();

    private static HttpResponseMessage Json(HttpStatusCode code, byte[] payload) => new(code)
    {
        Content = new StubContent(payload, null),
    };
    private static byte[] WithBom(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        return [0xEF, 0xBB, 0xBF, .. body];
    }

    [Fact]
    public async Task BomPrefixedSuccessResponseParses()
    {
        _handler.SetResponder(_ => Json(HttpStatusCode.OK,
            WithBom("""{"data":[{"index":0,"embedding":[0.25]}]}""")));

        using var embedding = new OpenAIEmbedding(
            model: "m", baseUrl: "http://localhost:9/v1", httpClient: _httpClient, allowLocalEndpoint: true);
        float[] vector = await embedding.EmbedAsync("x");

        Assert.Equal(new float[] { 0.25f }, vector);
    }

    [Fact]
    public async Task ErrorBodyIsPrefixReadAndTruncated()
    {
        // 100KB 错误体：仅读取前缀用于消息，不抛"响应体过大"。
        _handler.SetResponder(_ => Json(HttpStatusCode.BadGateway,
            Encoding.UTF8.GetBytes("{\"error\":\"" + new string('x', 100_000) + "\"}")));

        using var embedding = new OpenAIEmbedding(
            model: "m", baseUrl: "http://localhost:9/v1", httpClient: _httpClient, allowLocalEndpoint: true);
        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(() => embedding.EmbedAsync("x"));

        Assert.Contains("502", ex.Message);
        Assert.True(ex.Message.Length < 1024, $"错误消息不应包含整个错误体（实际 {ex.Message.Length} 字符）。");
    }

    [Fact]
    public async Task QwenAndJinaExposeEmbedAsync()
    {
        _handler.SetResponder(_ => Json(HttpStatusCode.OK,
            Encoding.UTF8.GetBytes("""{"data":[{"index":0,"embedding":[0.5,0.6]}]}""")));

        using var qwen = new QwenDenseEmbedding(dimension: 2, apiKey: "k", model: "m",
            baseUrl: "http://localhost:9/v1", httpClient: _httpClient, allowLocalEndpoint: true);
        Assert.Equal(new float[] { 0.5f, 0.6f }, await qwen.EmbedAsync("x"));

        using var jina = new JinaEmbedding(apiKey: "k", model: "m",
            baseUrl: "http://localhost:9/v1", httpClient: _httpClient, allowLocalEndpoint: true);
        Assert.Equal(new float[] { 0.5f, 0.6f }, await jina.EmbedAsync("x"));
    }
}
