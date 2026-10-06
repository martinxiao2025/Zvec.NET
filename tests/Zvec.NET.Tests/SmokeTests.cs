using Xunit;
using Zvec.NET;

namespace Zvec.NET.Tests;

public sealed class SmokeTests : IClassFixture<ZvecFixture>
{
    private readonly ZvecFixture _fixture;

    public SmokeTests(ZvecFixture fixture) => _fixture = fixture;

    [Fact]
    public void NativeLibraryLoads()
    {
        Assert.False(string.IsNullOrEmpty(Zvec.NativeVersion));
        Assert.True(Zvec.NativeVersionMajor >= 0);
    }

    [Fact]
    public void CreateInsertQueryRoundTrip()
    {
        string dir = _fixture.NewDir();
        var schema = new CollectionSchema("smoke")
            .AddField(new FieldSchema("title", DataType.String))
            .AddField(new FieldSchema("age", DataType.Int32))
            .AddVector(new VectorSchema("emb", DataType.VectorFp32, 4));

        using Collection collection = Zvec.CreateAndOpen(dir, schema);

        var docs = new List<Doc>
        {
            new("a", fields: new() { ["title"] = "alpha", ["age"] = 1 }, vectors: new() { ["emb"] = new float[] { 1, 0, 0, 0 } }),
            new("b", fields: new() { ["title"] = "beta", ["age"] = 2 }, vectors: new() { ["emb"] = new float[] { 0, 1, 0, 0 } }),
            new("c", fields: new() { ["title"] = "gamma", ["age"] = 3 }, vectors: new() { ["emb"] = new float[] { 0.9f, 0.1f, 0, 0 } }),
        };

        WriteResult[] results = collection.Insert(docs);
        Assert.All(results, r => Assert.True(r.Success));

        collection.Flush();
        Assert.Equal(3ul, collection.Stats.DocCount);

        // Fetch
        Dictionary<string, Doc> fetched = collection.Fetch(["a", "b"]);
        Assert.Equal(2, fetched.Count);
        Assert.Equal("alpha", fetched["a"].Field("title"));
        Assert.Equal(1, fetched["a"].Field("age"));
        Assert.Equal(4, ((float[])fetched["a"].Vector("emb")!).Length);

        // Vector query
        IReadOnlyList<Doc> found = collection.Query(new Query("emb", vector: new float[] { 1, 0, 0, 0 }), topk: 2);
        Assert.Equal(2, found.Count);
        Assert.Equal("a", found[0].Id);
        Assert.Equal("c", found[1].Id);

        // Delete
        Assert.True(collection.Delete("b").Success);
        Assert.Equal(2ul, collection.Stats.DocCount);

        collection.Close();
    }
}
