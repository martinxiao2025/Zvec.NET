using Xunit;
using Zvec.NET;

namespace Zvec.NET.Tests;

public sealed class CollectionDmlTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public CollectionDmlTests(ZvecFixture fixture) => _fixture = fixture;

    private static CollectionSchema BuildSchema() => new CollectionSchema("dml")
        .AddField(new FieldSchema("title", DataType.String))
        .AddField(new FieldSchema("age", DataType.Int32, nullable: true))
        .AddField(new FieldSchema("tags", DataType.ArrayString, nullable: true))
        .AddVector(new VectorSchema("emb", DataType.VectorFp32, 3));

    [Fact]
    public void UpsertInsertsThenUpdates()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());

        Assert.True(collection.Upsert(new Doc("x", fields: new() { ["title"] = "first" },
            vectors: new() { ["emb"] = new float[] { 1, 0, 0 } })).Success);
        Assert.True(collection.Upsert(new Doc("x", fields: new() { ["title"] = "second", ["age"] = 9 },
            vectors: new() { ["emb"] = new float[] { 0, 1, 0 } })).Success);

        Doc doc = collection.Fetch("x")["x"];
        Assert.Equal("second", doc.Field("title"));
        Assert.Equal(9, doc.Field("age"));
    }

    [Fact]
    public void UpdateChangesOnlyGivenFields()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(new Doc("u", fields: new() { ["title"] = "old", ["age"] = 5 },
            vectors: new() { ["emb"] = new float[] { 1, 1, 1 } }));

        Assert.True(collection.Update(new Doc("u", fields: new() { ["title"] = "new" })).Success);

        Doc doc = collection.Fetch("u")["u"];
        Assert.Equal("new", doc.Field("title"));
        Assert.Equal(5, doc.Field("age"));
    }

    [Fact]
    public void DeleteByFilterRemovesMatching()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(Enumerable.Range(0, 10).Select(i => new Doc("id" + i,
            fields: new() { ["title"] = "t" + i, ["age"] = i },
            vectors: new() { ["emb"] = new float[] { i, 0, 0 } })));

        collection.DeleteByFilter("age >= 5");
        Assert.Equal(5ul, collection.Stats.DocCount);
    }

    [Fact]
    public void IterateDocsStreamsAllDocuments()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(Enumerable.Range(0, 5).Select(i => new Doc("it" + i,
            fields: new() { ["title"] = "t" + i },
            vectors: new() { ["emb"] = new float[] { i, 1, 0 } })));

        List<Doc> docs = [.. collection.IterateDocs()];
        Assert.Equal(5, docs.Count);
        Assert.Contains(docs, d => d.Id == "it0");
    }

    [Fact]
    public void OutputFieldsLimitProjection()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(new Doc("p", fields: new() { ["title"] = "v", ["age"] = 3 },
            vectors: new() { ["emb"] = new float[] { 1, 2, 3 } }));
        // 落盘后的段才会应用 output_fields 投影（写入段行为由引擎决定）。
        collection.Flush();

        Dictionary<string, Doc> fetched = collection.Fetch(["p"], outputFields: ["title"]);
        Assert.Equal("v", fetched["p"].Field("title"));

        // includeVector=false：无向量字段。
        Dictionary<string, Doc> noVector = collection.Fetch(["p"], includeVector: false);
        Assert.False(noVector["p"].HasVector("emb"));
    }

    [Fact]
    public void NullableFieldsRoundTrip()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert(new Doc("n1",
            fields: new() { ["title"] = "x", ["age"] = null, ["tags"] = new[] { "a", "b" } },
            vectors: new() { ["emb"] = new float[] { 0, 0, 1 } }));

        Doc doc = collection.Fetch("n1")["n1"];
        Assert.Null(doc.Field("age"));
        Assert.Equal(new[] { "a", "b" }, (string[])doc.Field("tags")!);
    }

    [Fact]
    public void QueryByDocumentIdUsesStoredVector()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert([
            new Doc("q1", fields: new() { ["title"] = "a" }, vectors: new() { ["emb"] = new float[] { 1, 0, 0 } }),
            new Doc("q2", fields: new() { ["title"] = "b" }, vectors: new() { ["emb"] = new float[] { 0, 1, 0 } }),
        ]);

        IReadOnlyList<Doc> results = collection.Query(new Query("emb", id: "q1"), topk: 2);
        Assert.Equal("q1", results[0].Id);
    }

    [Fact]
    public void QueryWithFilterNarrowResults()
    {
        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(), BuildSchema());
        collection.Insert([
            new Doc("f1", fields: new() { ["title"] = "a", ["age"] = 1 }, vectors: new() { ["emb"] = new float[] { 1, 0, 0 } }),
            new Doc("f2", fields: new() { ["title"] = "b", ["age"] = 50 }, vectors: new() { ["emb"] = new float[] { 0.9f, 0.1f, 0 } }),
        ]);

        IReadOnlyList<Doc> results = collection.Query(
            new Query("emb", vector: new float[] { 1, 0, 0 }), topk: 10, filter: "age < 10");
        Assert.Single(results);
        Assert.Equal("f1", results[0].Id);
    }

    [Fact]
    public async Task AsyncApiRoundTrip()
    {
        string dir = _fixture.NewDir();
        using Collection collection = Zvec.CreateAndOpen(dir, BuildSchema());

        await collection.InsertAsync(new Doc("async1", fields: new() { ["title"] = "z" },
            vectors: new() { ["emb"] = new float[] { 1, 0, 0 } }));
        IReadOnlyList<Doc> results = await collection.QueryAsync(new Query("emb", vector: new float[] { 1, 0, 0 }));
        Assert.Equal("async1", results[0].Id);

        int count = 0;
        await foreach (Doc _ in collection.IterateDocsAsync())
        {
            count++;
        }

        Assert.Equal(1, count);
    }

    [Fact]
    public void ReopenReadsPersistedData()
    {
        string dir = _fixture.NewDir();
        using (Collection collection = Zvec.CreateAndOpen(dir, BuildSchema()))
        {
            collection.Insert(new Doc("persist", fields: new() { ["title"] = "keep" },
                vectors: new() { ["emb"] = new float[] { 1, 2, 3 } }));
            collection.Flush();
            collection.Close();
        }

        using Collection reopened = Zvec.Open(dir);
        Assert.Equal(1ul, reopened.Stats.DocCount);
        Assert.Equal("keep", reopened.Fetch("persist")["persist"].Field("title"));
        Assert.Single(reopened.Schema.Vectors);
        Assert.Equal(3, reopened.Schema.Fields.Count);
    }
}
