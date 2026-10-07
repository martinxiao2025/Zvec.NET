using Xunit;
using Zvec.NET;

namespace Zvec.NET.Tests;

/// <summary>P0 修复回归测试：编码缓冲堆退避、句柄租约、参数校验、异步迭代提前放弃。</summary>
public sealed class P0RegressionTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public P0RegressionTests(ZvecFixture fixture) => _fixture = fixture;

    private static CollectionSchema DenseSchema(string name) => new CollectionSchema(name)
        .AddField(new FieldSchema("title", DataType.String))
        .AddVector(new VectorSchema("emb", DataType.VectorFp32, 4));

    // =========================================================================
    // P0-1：大载荷编码缓冲退到非托管堆（原实现为无界 stackalloc，大字符串会打爆线程栈）
    // =========================================================================

    [Fact]
    public void LargeStringFieldRoundTrips()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), DenseSchema("bigstr"));

        // 超过栈缓冲阈值（1024 字节）的多字节 UTF-8 字符串。
        string large = string.Concat(Enumerable.Repeat("向量数据库zvec", 10_000)); // 50,000 字符 / 150,000 字节
        Assert.True(collection.Insert(new Doc("big",
            fields: new() { ["title"] = large },
            vectors: new() { ["emb"] = new float[] { 1, 0, 0, 0 } })).Success);

        string fetched = (string)collection.Fetch("big")["big"].Field("title")!;
        Assert.Equal(large, fetched);
    }

    [Fact]
    public void LargeStringArrayRoundTrips()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("bigarr")
            .AddField(new FieldSchema("tags", DataType.ArrayString)));

        string[] items = Enumerable.Range(0, 500).Select(i => $"标签-{i}").ToArray();
        Assert.True(collection.Insert(new Doc("a", fields: new() { ["tags"] = items })).Success);

        string[] fetched = (string[])collection.Fetch("a")["a"].Field("tags")!;
        Assert.Equal(items, fetched);
    }

    [Fact]
    public void LargeSparseVectorRoundTrips()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), new CollectionSchema("bigsparse")
            .AddField(new FieldSchema("t", DataType.String))
            .AddVector(new VectorSchema("sv", DataType.SparseVectorFp32, 0)));

        // 500 nnz → 4004 字节编码载荷（堆退避路径）；值从 0.5 起全非零（0 值元素会被引擎按稀疏语义丢弃）。
        // 顺带回归：引擎稀疏读回头为 8 字节（u64 nnz + 保留位），此前按 4 字节头解码导致整体错位 4 字节。
        const int Nnz = 500;
        var sparse = new SparseVector(
            Enumerable.Range(0, Nnz).Select(i => (uint)i).ToArray(),
            Enumerable.Range(0, Nnz).Select(i => (i + 1) * 0.5f).ToArray());
        Assert.True(collection.Insert(new Doc("s1",
            fields: new() { ["t"] = "x" },
            vectors: new() { ["sv"] = sparse })).Success);

        var fetched = (SparseVector)collection.Fetch("s1")["s1"].Vector("sv")!;
        Assert.Equal(Nnz, fetched.Count);
        for (int i = 0; i < Nnz; i++)
        {
            Assert.Equal((uint)i, fetched.Indices[i]);
            Assert.Equal((i + 1) * 0.5f, fetched.Values[i], 5);
        }
    }

    // =========================================================================
    // P0-2：句柄租约（并发安全与关闭后行为）
    // =========================================================================

    [Fact]
    public void OperationsAfterCloseThrowObjectDisposed()
    {
        Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), DenseSchema("closed"));
        collection.Insert(new Doc("a",
            fields: new() { ["title"] = "t" },
            vectors: new() { ["emb"] = new float[] { 1, 0, 0, 0 } }));
        collection.Close();

        var queryVector = new float[] { 1, 0, 0, 0 };
        Assert.Throws<ObjectDisposedException>(
            () => collection.Query(new Query("emb", vector: queryVector)));
        Assert.Throws<ObjectDisposedException>(() => collection.Fetch("a"));
        Assert.Throws<ObjectDisposedException>(() => collection.Stats);
        Assert.Throws<ObjectDisposedException>(() => collection.Flush());
        Assert.Throws<ObjectDisposedException>(
            () => collection.Upsert(new Doc("b",
                fields: new() { ["title"] = "t" },
                vectors: new() { ["emb"] = queryVector })));
    }

    [Fact]
    public async Task ConcurrentReaderAndWriterShareCollectionSafely()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), DenseSchema("conc"));
        var queryVector = new float[] { 1, 0, 0, 0 };
        for (int i = 0; i < 10; i++)
        {
            collection.Insert(new Doc($"d{i}",
                fields: new() { ["title"] = $"t{i}" },
                vectors: new() { ["emb"] = queryVector }));
        }

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 25; i++)
            {
                Assert.NotEmpty(collection.Query(new Query("emb", vector: queryVector), topk: 3));
            }
        }));
        Task writer = Task.Run(() =>
        {
            for (int i = 0; i < 50; i++)
            {
                collection.Upsert(new Doc($"w{i}",
                    fields: new() { ["title"] = $"t{i}" },
                    vectors: new() { ["emb"] = queryVector }));
            }
        });

        await Task.WhenAll([.. readers, writer]);
        Assert.True(collection.Stats.DocCount >= 10);
    }

    // =========================================================================
    // P0-3：FlatQueryParam 属性生效（此前 IsUsingRefiner/ScaleFactor 被硬编码忽略）
    // =========================================================================

    [Fact]
    public void FlatQueryParamPropertiesArePassedThrough()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), DenseSchema("flatp"));
        collection.Insert(new Doc("a",
            fields: new() { ["title"] = "ta" },
            vectors: new() { ["emb"] = new float[] { 1, 0, 0, 0 } }));
        collection.Insert(new Doc("b",
            fields: new() { ["title"] = "tb" },
            vectors: new() { ["emb"] = new float[] { 0, 1, 0, 0 } }));

        IReadOnlyList<Doc> hits = collection.Query(
            new Query("emb", vector: new float[] { 1, 0, 0, 0 },
                param: new FlatQueryParam(scaleFactor: 25f, isUsingRefiner: true)),
            topk: 2);
        Assert.Equal(2, hits.Count);
        Assert.Equal("a", hits[0].Id);
    }

    // =========================================================================
    // P0-4：异步迭代提前放弃不再泄漏生产者任务（原实现会永久阻塞在有界 Channel 上）
    // =========================================================================

    [Fact]
    public async Task IterateDocsAsyncEarlyAbandonCompletes()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), DenseSchema("iter"));
        for (int i = 0; i < 200; i++)
        {
            collection.Insert(new Doc($"d{i}",
                fields: new() { ["title"] = $"t{i}" },
                vectors: new() { ["emb"] = new float[] { 1, 0, 0, 0 } }));
        }

        // 安全网：修复缺失时生产者会填满 64 槽位后阻塞，本测试将超时失败而非无限挂起。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        int seen = 0;
        await foreach (Doc _ in collection.IterateDocsAsync(cancellationToken: timeout.Token))
        {
            if (++seen == 5)
            {
                break;
            }
        }

        Assert.Equal(5, seen);
    }

    // =========================================================================
    // P0-5：null 参数抛 ArgumentNullException（原实现抛 NullReferenceException）
    // =========================================================================

    [Fact]
    public void NullArgumentsThrowArgumentNull()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), DenseSchema("nullchk"));

        // null 输入经局部变量传入；各入口先绑定为委托再触发，
        // 断言库在参数边界抛 ArgumentNullException 而非 NullReferenceException。
        string? nullFilter = null;
        string? nullName = null;
        string? nullExpression = null;
        string? nullFieldName = null;
        IndexParam? nullIndexParam = null;
        var queryVector = new float[] { 1, 0, 0, 0 };

        // 字段名为 null 的检索请求经目标类型 new 构造（覆盖 ValidateIdentifier 的 null 防护）。
        Query nullFieldQuery = new(nullFieldName!) { Vector = queryVector };

        Action<string> deleteByFilter = collection.DeleteByFilter;
        Action<string, IndexParam> createIndex = collection.CreateIndex;
        Action<FieldSchema, string> addColumn = collection.AddColumn;
        Action<string> dropIndex = collection.DropIndex;
        Action<string> dropColumn = collection.DropColumn;
        Action<string, string?, FieldSchema?> alterColumn = collection.AlterColumn;
        Func<Query?, int, string?, bool, IReadOnlyList<string>?, IReRanker?, IReadOnlyList<Doc>> lookup = collection.Query;

        Assert.Throws<ArgumentNullException>(() => deleteByFilter(nullFilter!));
        Assert.Throws<ArgumentNullException>(() => createIndex(nullName!, new FlatIndexParam()));
        Assert.Throws<ArgumentNullException>(() => createIndex("emb", nullIndexParam!));
        Assert.Throws<ArgumentNullException>(
            () => addColumn(new FieldSchema("extra", DataType.Int32), nullExpression!));
        Assert.Throws<ArgumentNullException>(() => dropIndex(nullName!));
        Assert.Throws<ArgumentNullException>(() => dropColumn(nullName!));
        Assert.Throws<ArgumentNullException>(() => alterColumn(nullName!, null, null));
        Assert.Throws<ArgumentNullException>(() => lookup(nullFieldQuery, 10, null, false, null, null));
    }
}
