using Xunit;
using Zvec.NET;
using Zvec.NET.Embedding;

namespace Zvec.NET.Tests;

public sealed class RerankerTests
{
    private static Doc DocOf(string id, float score) => new(id, score);

    [Fact]
    public void RrfFusesTwoRoutes()
    {
        var route1 = new[] { DocOf("a", 0.9f), DocOf("b", 0.8f) };
        var route2 = new[] { DocOf("b", 0.7f), DocOf("c", 0.6f) };

        IReadOnlyList<Doc> merged = new RrfReRanker().Rerank([route1, route2], topk: 3);

        // b 在两路都出现，RRF 应将其排到最前。
        Assert.Equal("b", merged[0].Id);
        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void WeightedRequiresMatchingWeights()
    {
        var route = new[] { DocOf("a", 1f) };
        Assert.Throws<ArgumentException>(() => new WeightedReRanker([0.5, 0.5]).Rerank([route], 1));
    }

    [Fact]
    public void WeightedCombinesScores()
    {
        var route1 = new[] { DocOf("a", 1f), DocOf("b", 0f) };
        var route2 = new[] { DocOf("b", 1f), DocOf("a", 0f) };

        IReadOnlyList<Doc> merged = new WeightedReRanker([1.0, 3.0]).Rerank([route1, route2], topk: 2);
        Assert.Equal("b", merged[0].Id);
    }

    [Fact]
    public void CallbackReRankerDelegates()
    {
        var route = new[] { DocOf("a", 1f) };
        IReadOnlyList<Doc> result = new CallbackReRanker((routes, topk) => routes[0]).Rerank([route], 1);
        Assert.Equal("a", result[0].Id);
    }
}

public sealed class EmbeddingSecurityTests
{
    [Theory]
    [InlineData("http://localhost:1234/v1/embeddings")]
    [InlineData("http://127.0.0.1:8080/x")]
    [InlineData("http://[::1]:9000/x")]
    [InlineData("http://[::]:9000/x")]                        // IPv6 未指定地址（Linux 视作环回）
    [InlineData("http://[::ffff:127.0.0.1]:9000/x")]     // IPv4-mapped 环回
    [InlineData("http://[::ffff:10.1.2.3]/v1/x")]         // IPv4-mapped 私网
    [InlineData("http://[64:ff9b::10.1.2.3]/x")]          // NAT64 承载私网 IPv4
    [InlineData("http://[64:ff9b:1::10.1.2.3]/x")]        // RFC 8215 本地 NAT64 承载私网
    [InlineData("http://[2002:0a01:0203::]/x")]           // 6to4 承载 10.1.2.3
    [InlineData("http://[::10.1.2.3]/x")]                 // IPv4 兼容格式承载私网
    [InlineData("http://[2001:0::5cdf]/x")]               // Teredo
    [InlineData("http://10.1.2.3/v1/x")]
    [InlineData("http://172.16.0.1/v1/x")]
    [InlineData("http://192.168.1.5/v1/x")]
    [InlineData("http://169.254.1.1/v1/x")]
    [InlineData("http://100.64.1.1/v1/x")]                // 100.64.0.0/10 CGNAT/共享地址空间
    [InlineData("http://198.18.0.1/v1/x")]                // 198.18.0.0/15 基准测试
    [InlineData("http://0.0.0.0/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("file://api.openai.com/v1")]
    [InlineData("ftp://example.com/x")]
    public async Task ValidateRequestUriRejectsDisallowedHosts(string url)
    {
        await Assert.ThrowsAnyAsync<Exception>(
            () => EmbeddingHttpClientBase.ValidateRequestUriAsync(new Uri(url)));
    }

    [Fact]
    public async Task ValidateRequestUriAllowsPublicHost()
    {
        // api.openai.com 解析为公网地址；仅验证不抛异常。
        await EmbeddingHttpClientBase.ValidateRequestUriAsync(new Uri("https://api.openai.com/v1/embeddings"));
    }

    [Fact]
    public void OpenAIEmbeddingRejectsLocalBaseUrl()
    {
        // URL 安全校验先于任何网络请求，无需配置凭据。
        using var embedding = new OpenAIEmbedding(baseUrl: "http://localhost:1234/v1");
        Assert.Throws<NotSupportedException>(() => embedding.Embed("hi"));
    }
}

public sealed class BM25EmbeddingTests
{
    private static readonly string[] Corpus =
    [
        "机器学习 是 人工智能 的 分支",
        "深度 学习 使用 神经 网络",
        "the vector database stores embeddings",
    ];

    [Fact]
    public void QueryEncodesKnownTermsOnly()
    {
        var bm25 = new BM25Embedding(Corpus, encodingType: "query");
        SparseVector sparse = bm25.Embed("机器学习 database");

        Assert.True(sparse.Count >= 2);
        Assert.All(sparse.Values, v => Assert.True(v > 0));
    }

    [Fact]
    public void DocumentModeWeightsFrequentTermsHigher()
    {
        var bm25 = new BM25Embedding(Corpus, encodingType: "document");
        SparseVector sparse = bm25.Embed("vector vector database");

        // 两个不同词项；词频更高的 vector 权重应高于 database。
        Assert.Equal(2, sparse.Count);
        Assert.True(bm25.TryGetTermId("vector", out uint vectorId));
        Assert.True(bm25.TryGetTermId("database", out uint databaseId));
        float vectorWeight = sparse.Values[Array.IndexOf(sparse.Indices, vectorId)];
        float databaseWeight = sparse.Values[Array.IndexOf(sparse.Indices, databaseId)];
        Assert.True(vectorWeight > databaseWeight);
    }

    [Fact]
    public void TokenizerSplitsCjkAndLatin()
    {
        IReadOnlyList<string> tokens = BM25Embedding.Tokenize("Hello 世界123 foo_bar");
        Assert.Contains("hello", tokens);
        Assert.Contains("世", tokens);
        Assert.Contains("界", tokens);
        Assert.Contains("123", tokens);
        Assert.Contains("foo", tokens);
        Assert.Contains("bar", tokens);
    }

    [Fact]
    public void EmptyCorpusThrows()
    {
        Assert.Throws<ArgumentException>(() => new BM25Embedding([]));
    }
}

public sealed class ErrorPathTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public ErrorPathTests(ZvecFixture fixture) => _fixture = fixture;

    [Fact]
    public void OpenNonexistentPathFails()
    {
        string missing = Path.Combine(_fixture.NewDir(), "not-exists");
        Assert.Throws<Interop.ZvecException>(() => Zvec.Open(missing));
    }

    [Fact]
    public void CreateOverExistingPathFails()
    {
        string dir = _fixture.NewDir();
        using (Collection _ = Zvec.CreateAndOpen(dir, new CollectionSchema("errc1")
            .AddField(new FieldSchema("t", DataType.String))))
        {
        }

        Assert.Throws<Interop.ZvecException>(() => Zvec.CreateAndOpen(dir, new CollectionSchema("errc1")
            .AddField(new FieldSchema("t", DataType.String))));
    }

    [Fact]
    public void InsertDocWithUnknownFieldThrows()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("errc2")
            .AddField(new FieldSchema("t", DataType.String)));

        Assert.Throws<ArgumentException>(() => collection.Insert(
            new Doc("x", fields: new() { ["unknown"] = 1 })));
    }

    [Fact]
    public void InvalidFilterExpressionFails()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("errc3")
            .AddField(new FieldSchema("age", DataType.Int32)));

        // 语法错误的表达式由引擎决定是否报错；空表达式在绑定层即被拒绝。
        Assert.Throws<ArgumentException>(() => collection.DeleteByFilter(""));
        Assert.Throws<ArgumentException>(() => collection.Query(topk: 1, filter: ""));
    }

    [Fact]
    public void NullByteInFilterRejected()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("errc4")
            .AddField(new FieldSchema("age", DataType.Int32)));

        Assert.Throws<ArgumentException>(() => collection.DeleteByFilter("age \0 > 1"));
    }

    [Fact]
    public void DoubleInitThrows()
    {
        Assert.True(Zvec.IsInitialized);
        Assert.Throws<Interop.ZvecException>(() => Zvec.Init());
    }

    [Fact]
    public void QueryMissingVectorFieldThrows()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("errc5")
            .AddField(new FieldSchema("t", DataType.String)));

        // 绑定层先行校验：字段不存在时抛 ArgumentException。
        Assert.Throws<ArgumentException>(() =>
            collection.Query(new Query("nope", vector: new float[] { 1 })));
    }
}
