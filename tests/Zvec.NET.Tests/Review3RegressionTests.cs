using Xunit;
using Zvec.NET;
using Zvec.NET.Embedding;

namespace Zvec.NET.Tests;

/// <summary>第三轮评审修复的回归测试：score=0 语义、迭代器/Close 竞态、选项校验、
/// schema 重名、RRF 参数、按 id 查询的错误路径。</summary>
public sealed class Review3RegressionTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public Review3RegressionTests(ZvecFixture fixture) => _fixture = fixture;

    private Collection CreateZeroScoreCollection()
    {
        Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(),
            new CollectionSchema("zero_score_c1")
                .AddField(new FieldSchema("t", DataType.String))
                .AddVector(new VectorSchema("emb", DataType.VectorFp32, 2)));
        collection.Insert(new Doc("z", fields: new() { ["t"] = "x" },
            vectors: new() { ["emb"] = new float[] { 1, 0 } }));
        collection.CreateIndex("emb", new HnswIndexParam(MetricType.L2, m: 4, efConstruction: 16));
        return collection;
    }

    [Fact]
    public void QueryResultWithExactZeroScoreKeepsScore()
    {
        using Collection collection = CreateZeroScoreCollection();

        IReadOnlyList<Doc> results = collection.Query(new Query("emb", vector: new float[] { 1, 0 }), topk: 1);

        // L2 完美匹配得分恰为 0：查询结果路径不得把 0 分当作"无得分"丢弃。
        Assert.Equal("z", results[0].Id);
        Assert.NotNull(results[0].Score);
        Assert.Equal(0f, results[0].Score!.Value, 1e-6f);
    }

    [Fact]
    public void FetchedAndIteratedDocsCarryNoScore()
    {
        using Collection collection = CreateZeroScoreCollection();

        Assert.Null(collection.Fetch("z")["z"].Score);
        Assert.All(collection.IterateDocs(), doc => Assert.Null(doc.Score));
    }

    [Fact]
    public void ConcurrentCloseDefersUntilIteratorFinishes()
    {
        using Collection collection = CreateZeroScoreCollection();
        collection.Insert([
            new Doc("i1", fields: new() { ["t"] = "a" }, vectors: new() { ["emb"] = new float[] { 0, 1 } }),
            new Doc("i2", fields: new() { ["t"] = "b" }, vectors: new() { ["emb"] = new float[] { 0, 1 } }),
        ]);

        using IEnumerator<Doc> enumerator = collection.IterateDocs().GetEnumerator();
        Assert.True(enumerator.MoveNext());

        // 迭代器仍打开时 Close：真正的原生 close 被推迟（租约保护），迭代继续可用。
        collection.Close();
        Assert.False(collection.IsClosed);
        Assert.True(enumerator.MoveNext());

        // 迭代结束释放租约后原生 close 才执行（成功而非句柄泄漏）。
        enumerator.Dispose();
        Assert.True(collection.IsClosed);
    }

    [Fact]
    public void QueryByMissingDocIdThrowsArgumentException()
    {
        using Collection collection = CreateZeroScoreCollection();

        // 此前为无上下文的 KeyNotFoundException；现在是带 paramName 的 ArgumentException。
        Assert.Throws<ArgumentException>(() => collection.Query(new Query("emb", id: "missing")));
    }

    [Fact]
    public void NegativeEngineOptionsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Zvec.Init(new ZvecOptions { QueryThreads = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Zvec.Init(new ZvecOptions { MemoryLimitMb = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Zvec.Init(new ZvecOptions { LogFileSize = -5 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Zvec.Init(new ZvecOptions { BruteForceByKeysRatio = 1.5f }));
    }

    [Fact]
    public void DuplicateSchemaFieldNamesRejected()
    {
        var schema = new CollectionSchema("dup_c1").AddField(new FieldSchema("a", DataType.String));
        Assert.Throws<ArgumentException>(() => schema.AddField(new FieldSchema("a", DataType.Int32)));
        Assert.Throws<ArgumentException>(() => schema.AddVector(new VectorSchema("a", DataType.VectorFp32, 2)));
    }

    [Fact]
    public void RrfRankConstantMustBePositive() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RrfReRanker(0));

    [Fact]
    public void RrfSameIdAcrossRoutesKeepsFirstInstance()
    {
        // 同 Id 在两路携带不同标量：RRF 胜出实例应与 Weighted 一致（首次出现），可预测。
        var route1 = new[] { new Doc("a", 0.9f, fields: new() { ["t"] = "first" }) };
        var route2 = new[] { new Doc("a", 0.8f, fields: new() { ["t"] = "second" }) };

        IReadOnlyList<Doc> merged = new RrfReRanker().Rerank([route1, route2], topk: 1);

        Assert.Equal("first", merged[0].Field("t"));
    }

    [Fact]
    public void GroupByQueryDoesNotOverflowSilently()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(),
            new CollectionSchema("group_c1")
                .AddField(new FieldSchema("cat", DataType.String))
                .AddVector(new VectorSchema("emb", DataType.VectorFp32, 2)));
        collection.Insert(new Doc("g1", fields: new() { ["cat"] = "x" },
            vectors: new() { ["emb"] = new float[] { 1, 0 } }));

        // 放大 topk 溢出应显式失败，而不是把负数传给引擎。
        Assert.Throws<OverflowException>(() => collection.GroupByQuery(
            new Query("emb", vector: new float[] { 1, 0 }), "cat",
            groupCount: int.MaxValue / 2, topkPerGroup: 8));
    }
}
