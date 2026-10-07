using Xunit;
using Zvec.NET;

namespace Zvec.NET.Tests;

public sealed class CollectionDdlTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public CollectionDdlTests(ZvecFixture fixture) => _fixture = fixture;

    private static CollectionSchema BuildSchema() => new CollectionSchema("ddl")
        .AddField(new FieldSchema("title", DataType.String))
        .AddField(new FieldSchema("age", DataType.Int32))
        .AddVector(new VectorSchema("emb", DataType.VectorFp32, 4));

    [Fact]
    public void CreateAndDropHnswIndex()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(Enumerable.Range(0, 20).Select(i => new Doc("d" + i,
            fields: new() { ["title"] = "t", ["age"] = i },
            vectors: new() { ["emb"] = new float[] { i * 0.1f, 1, 0, 0 } })));

        collection.CreateIndex("emb", new HnswIndexParam(MetricType.Cosine, m: 8, efConstruction: 64));
        Assert.Contains(collection.Stats.Indexes, index => index.Name == "emb");

        collection.DropIndex("emb");
    }

    [Fact]
    public void CreateInvertIndexOnScalarField()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(Enumerable.Range(0, 6).Select(i => new Doc("i" + i,
            fields: new() { ["title"] = "t", ["age"] = i },
            vectors: new() { ["emb"] = new float[] { i * 0.1f, 1, 0, 0 } })));

        // 标量倒排索引不进入 Stats.Indexes（其中仅列出向量索引），以过滤查询可用来验证。
        collection.CreateIndex("age", new InvertIndexParam(enableRangeOptimization: true));

        IReadOnlyList<Doc> results = collection.Query(topk: 10, filter: "age >= 4");
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void AddDropColumnUpdatesSchema()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(new Doc("a1", fields: new() { ["title"] = "x", ["age"] = 1 },
            vectors: new() { ["emb"] = new float[] { 1, 0, 0, 0 } }));

        collection.AddColumn(new FieldSchema("extra", DataType.Double, nullable: true), "0");
        Assert.NotNull(collection.Schema.Field("extra"));

        Doc doc = collection.Fetch("a1")["a1"];
        Assert.Equal(0d, doc.Field("extra"));

        collection.DropColumn("extra");
        Assert.Null(collection.Schema.Field("extra"));
    }

    [Fact]
    public void AlterColumnRenames()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(new Doc("a1", fields: new() { ["title"] = "x", ["age"] = 1 },
            vectors: new() { ["emb"] = new float[] { 1, 0, 0, 0 } }));

        collection.AlterColumn("age", newName: "years");
        Assert.Null(collection.Schema.Field("age"));
        Assert.NotNull(collection.Schema.Field("years"));
        Assert.Equal(1, collection.Fetch("a1")["a1"].Field("years"));
    }

    [Fact]
    public void OptimizeRuns()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(Enumerable.Range(0, 10).Select(i => new Doc("o" + i,
            fields: new() { ["title"] = "t", ["age"] = i },
            vectors: new() { ["emb"] = new float[] { i * 0.5f, 0, 1, 0 } })));
        collection.Optimize();
        Assert.Equal(10ul, collection.Stats.DocCount);
    }
}

public sealed class SparseVectorTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public SparseVectorTests(ZvecFixture fixture) => _fixture = fixture;

    private static CollectionSchema BuildSchema() => new CollectionSchema("sparse")
        .AddField(new FieldSchema("title", DataType.String))
        .AddVector(new VectorSchema("dense", DataType.VectorFp32, 3))
        .AddVector(new VectorSchema("sparse", DataType.SparseVectorFp32, 0));

    [Fact]
    public void SparseInsertAndQueryRoundTrip()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.CreateIndex("sparse", new FlatIndexParam(MetricType.Ip));

        collection.Insert([
            new Doc("s1", fields: new() { ["title"] = "apple" },
                vectors: new()
                {
                    ["dense"] = new float[] { 1, 0, 0 },
                    ["sparse"] = new SparseVector([2u, 7u], [0.5f, 0.9f]),
                }),
            new Doc("s2", fields: new() { ["title"] = "banana" },
                vectors: new()
                {
                    ["dense"] = new float[] { 0, 1, 0 },
                    ["sparse"] = new SparseVector([3u, 7u], [0.4f, 0.8f]),
                }),
        ]);

        // 读回稀疏向量（含内容校验：引擎读回侧头为 8 字节，曾因按 4 字节解码导致错位）。
        Doc fetched = collection.Fetch("s1")["s1"];
        var sparse = Assert.IsType<SparseVector>(fetched.Vector("sparse"));
        Assert.Equal(2, sparse.Count);
        Assert.Equal(new uint[] { 2, 7 }, sparse.Indices);
        Assert.Equal(0.5f, sparse.Values[0], 5);
        Assert.Equal(0.9f, sparse.Values[1], 5);

        // 稀疏检索（内部走 MultiQuery 路径）。
        IReadOnlyList<Doc> results = collection.Query(
            new Query("sparse", vector: new SparseVector([2u], [1.0f])), topk: 2);
        Assert.Equal(2, results.Count);
        Assert.Equal("s1", results[0].Id);
    }

    [Fact]
    public void MultiQueryWithRrfMergesRoutes()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert([
            new Doc("m1", fields: new() { ["title"] = "x" },
                vectors: new() { ["dense"] = new float[] { 1, 0, 0 }, ["sparse"] = new SparseVector([1u], [1f]) }),
            new Doc("m2", fields: new() { ["title"] = "y" },
                vectors: new() { ["dense"] = new float[] { 0, 1, 0 }, ["sparse"] = new SparseVector([2u], [1f]) }),
        ]);

        IReadOnlyList<Doc> merged = collection.Query(
        [
            new Query("dense", vector: new float[] { 1, 0, 0 }),
            new Query("sparse", vector: new SparseVector([2u], [1f])),
        ], topk: 2, reranker: new RrfReRanker());
        Assert.Equal(2, merged.Count);
    }
}

public sealed class FullTextSearchTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public FullTextSearchTests(ZvecFixture fixture) => _fixture = fixture;

    [Fact]
    public void FtsIndexAndMatchQuery()
    {
        var schema = new CollectionSchema("fts")
            .AddField(new FieldSchema("content", DataType.String, indexParam: new FtsIndexParam("standard")))
            .AddVector(new VectorSchema("emb", DataType.VectorFp32, 2));

        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), schema);
        collection.Insert([
            new Doc("t1", fields: new() { ["content"] = "vector database for search" },
                vectors: new() { ["emb"] = new float[] { 1, 0 } }),
            new Doc("t2", fields: new() { ["content"] = "relational database mysql" },
                vectors: new() { ["emb"] = new float[] { 0, 1 } }),
        ]);

        IReadOnlyList<Doc> results = collection.Query(
            new Query("content", fts: new Fts(matchString: "vector search")), topk: 2);
        Assert.NotEmpty(results);
        Assert.Equal("t1", results[0].Id);
    }

    [Fact]
    public void GroupByQueryClientSide()
    {
        var schema = new CollectionSchema("group")
            .AddField(new FieldSchema("category", DataType.String))
            .AddVector(new VectorSchema("emb", DataType.VectorFp32, 2));

        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), schema);
        collection.Insert([
            new Doc("g1", fields: new() { ["category"] = "fruit" }, vectors: new() { ["emb"] = new float[] { 1, 0 } }),
            new Doc("g2", fields: new() { ["category"] = "fruit" }, vectors: new() { ["emb"] = new float[] { 0.9f, 0.1f } }),
            new Doc("g3", fields: new() { ["category"] = "tool" }, vectors: new() { ["emb"] = new float[] { 0, 1 } }),
            new Doc("g4", fields: new() { ["category"] = "tool" }, vectors: new() { ["emb"] = new float[] { 0.1f, 0.9f } }),
        ]);

        IReadOnlyList<GroupResult> groups = collection.GroupByQuery(
            new Query("emb", vector: new float[] { 1, 0 }), "category", groupCount: 2, topkPerGroup: 1);
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Single(g.Docs));
    }
}
