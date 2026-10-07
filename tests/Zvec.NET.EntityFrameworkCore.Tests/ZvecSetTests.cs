using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Zvec.NET;
using Zvec.NET.EntityFrameworkCore;

namespace Zvec.NET.EntityFrameworkCore.Tests;

[Collection("ef-engine")]
public sealed class ZvecSetTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "zvec-ef-tests-" + Guid.NewGuid().ToString("N"));

    static ZvecSetTests()
    {
        if (!global::Zvec.NET.Zvec.IsInitialized)
        {
            global::Zvec.NET.Zvec.Init(new ZvecOptions { LogLevel = LogLevel.Error });
        }

        Directory.CreateDirectory(Root);
    }

    private static string NewDir() => Path.Combine(Root, Guid.NewGuid().ToString("N"));

    [VectorCollection("products")]
    public class Product
    {
        [VectorKey]
        public string Sku { get; set; } = "";

        public string Name { get; set; } = "";

        public double Price { get; set; }

        public int Stock { get; set; }

        [VectorField(Dimension = 4)]
        public float[]? Embedding { get; set; }

        [VectorIgnored]
        public byte[]? InternalBlob { get; set; }
    }

    private sealed class ShopContext : DbContext
    {
        public DbSet<Product> Products => Set<Product>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseInMemoryDatabase("shop-" + Guid.NewGuid().ToString("N"));

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            // EF 主键与向量主键（[VectorKey]）对齐到同一属性。
            modelBuilder.Entity<Product>().HasKey(p => p.Sku);
    }

    [Fact]
    public void CreateUpsertSearchRoundTrip()
    {
        using ZvecSet<Product> set = ZvecSet<Product>.Create(NewDir(), options => options
            .WithVectorIndex(p => p.Embedding, new HnswIndexParam(MetricType.Cosine, m: 8, efConstruction: 64)));

        Assert.Equal("products", set.Underlying.Schema.Name);
        Assert.Equal(4, (int)set.Underlying.Schema.Vectors[0].Dimension);
        Assert.Equal(3, set.Underlying.Schema.Fields.Count); // Name / Price / Stock

        Assert.True(set.Upsert(new Product
        {
            Sku = "a",
            Name = "apple",
            Price = 9.9,
            Stock = 10,
            Embedding = [1, 0, 0, 0],
        }).Success);

        set.UpsertRange([
            new Product { Sku = "b", Name = "banana", Price = 19.9, Stock = 5, Embedding = [0, 1, 0, 0] },
            new Product { Sku = "c", Name = "cherry", Price = 29.9, Stock = 0, Embedding = [0.9f, 0.1f, 0, 0] },
        ]);

        IReadOnlyList<SearchHit> hits = set.Search([1, 0, 0, 0], topk: 3, filter: "Price < 25");
        Assert.Equal(2, hits.Count);
        Assert.Equal("a", hits[0].Id);
        Assert.Equal("b", hits[1].Id);

        // ignored 字段与 key 不出现在 doc 字段中。
        Assert.Equal("apple", hits[0].Doc.Field("Name"));
        Assert.False(hits[0].Doc.HasField("InternalBlob"));
        Assert.False(hits[0].Doc.HasField("Sku"));

        Assert.True(set.Delete(new Product { Sku = "b" }).Success);
        Assert.Equal(2ul, set.Underlying.Stats.DocCount);
    }

    [Fact]
    public async Task FindSimilarJoinsBackToEfEntities()
    {
        string dir = NewDir();
        using ZvecSet<Product> set = ZvecSet<Product>.Create(dir);

        using var context = new ShopContext();
        var products = new List<Product>
        {
            new() { Sku = "x1", Name = "phone", Price = 999, Stock = 1, Embedding = [1, 0, 0, 0] },
            new() { Sku = "x2", Name = "laptop", Price = 1999, Stock = 2, Embedding = [0.95f, 0.05f, 0, 0] },
            new() { Sku = "x3", Name = "pen", Price = 5, Stock = 100, Embedding = [0, 0, 1, 0] },
        };
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
        set.UpsertRange(products);

        List<SearchHit<Product>> similar = await set.FindSimilarAsync(context, [1, 0, 0, 0], topk: 2);

        Assert.Equal(2, similar.Count);
        Assert.Equal("phone", similar[0].Entity.Name);
        Assert.Equal("laptop", similar[1].Entity.Name);
        Assert.True(similar[0].Score >= similar[1].Score);
    }

    [Fact]
    public void ReopenExistingSetAndSearch()
    {
        string dir = NewDir();
        using (ZvecSet<Product> created = ZvecSet<Product>.Create(dir))
        {
            created.Upsert(new Product { Sku = "r1", Name = " reused", Price = 1, Stock = 1, Embedding = [0, 1, 0, 0] });
            created.Underlying.Flush();
        }

        using ZvecSet<Product> reopened = ZvecSet<Product>.Open(dir);
        Assert.Equal("products", reopened.Underlying.Schema.Name);
        IReadOnlyList<SearchHit> hits = reopened.Search([0, 1, 0, 0], topk: 1);
        Assert.Equal("r1", hits[0].Id);
    }

    [Fact]
    public void SparseVectorPropertyRoundTrip()
    {
        using ZvecSet<Article> set = ZvecSet<Article>.Create(NewDir());
        set.Upsert(new Article
        {
            Code = 42, // int 键
            Title = "sparse",
            Keywords = new SparseVector([1u, 4u], [0.5f, 0.9f]),
        });

        Assert.Equal("42", set.Underlying.Fetch("42")["42"].Id);
        IReadOnlyList<SearchHit> hits = set.Search(new SparseVector([1u], [1f]), topk: 1);
        Assert.Equal("42", hits[0].Id);
    }

    [VectorCollection("articles")]
    public class Article
    {
        [VectorKey]
        public int Code { get; set; }

        public string Title { get; set; } = "";

        [VectorField]
        public SparseVector? Keywords { get; set; }
    }

    [Fact]
    public void ServiceCollectionRegistersSingletonSets()
    {
        string dir = NewDir();
        var services = new ServiceCollection();
        services.AddZvecSets(
            configureEngine: options => options.LogLevel = LogLevel.Error,
            configureSets: sets => sets.AddSet<Product>(dir));

        using ServiceProvider provider = services.BuildServiceProvider();
        IZvecSet<Product> set = provider.GetRequiredService<IZvecSet<Product>>();
        set.Upsert(new Product { Sku = "d1", Name = "di", Price = 3, Stock = 1, Embedding = [1, 1, 0, 0] });

        // 单例：两次解析同一实例。
        Assert.Same(set, provider.GetRequiredService<IZvecSet<Product>>());
        IReadOnlyList<SearchHit> hits = set.Search([1, 1, 0, 0], topk: 1);
        Assert.Equal("d1", hits[0].Id);
    }

    [Fact]
    public void EntityWithoutKeyThrows()
    {
        Assert.Throws<InvalidOperationException>(() => ZvecSet<NoKey>.Create(NewDir()));
    }

    public class NoKey
    {
        public string Name { get; set; } = "";

        [VectorField(Dimension = 2)]
        public float[]? Emb { get; set; }
    }

    [Fact]
    public void WithVectorIndexOnNonVectorPropertyThrows()
    {
        Assert.Throws<ArgumentException>(() => ZvecSet<Product>.Create(NewDir(), options => options
            .WithVectorIndex(p => p.Name, new HnswIndexParam())));
    }

    [Fact]
    public void EmptyOrNullByteFilterRejectedAtEfLayer()
    {
        using ZvecSet<Product> set = ZvecSet<Product>.Create(NewDir());
        set.Upsert(new Product { Sku = "f1", Name = "n", Price = 1, Stock = 1, Embedding = [1, 0, 0, 0] });

        Assert.Throws<ArgumentException>(() => set.Search([1, 0, 0, 0], filter: ""));
        Assert.Throws<ArgumentException>(() => set.Search([1, 0, 0, 0], filter: "Price > 1\0"));
    }

    // ============ 第三轮评审回归：键裁决 / 可空键 / 空键 / 异步写入 / 取消 ============

    public class AttrAndConvention
    {
        public int Id { get; set; } // 约定键存在：显式 [VectorKey] 应胜出

        [VectorKey]
        public string Code { get; set; } = "";

        [VectorField(Dimension = 2)]
        public float[]? Emb { get; set; }
    }

    [Fact]
    public void AttributeKeyWinsOverConventionId()
    {
        using ZvecSet<AttrAndConvention> set = ZvecSet<AttrAndConvention>.Create(NewDir());
        set.Upsert(new AttrAndConvention { Id = 7, Code = "k1", Emb = [1, 0] });

        Assert.Equal("k1", set.KeyOf(new AttrAndConvention { Code = "k1" }));
        Assert.True(set.Underlying.Fetch("k1").ContainsKey("k1"));
    }

    public class TwoVectorKeys
    {
        [VectorKey] public string A { get; set; } = "";

        [VectorKey] public string B { get; set; } = "";

        [VectorField(Dimension = 2)] public float[]? Emb { get; set; }
    }

    [Fact]
    public void MultipleVectorKeyAttributesThrow()
    {
        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => ZvecSet<TwoVectorKeys>.Create(NewDir()));
        Assert.Contains("多个 [VectorKey]", ex.Message);
    }

    public class TwoConventionKeys
    {
        public int Id { get; set; }

        public int TwoConventionKeysId { get; set; }

        [VectorField(Dimension = 2)] public float[]? Emb { get; set; }
    }

    [Fact]
    public void AmbiguousConventionKeysThrow()
    {
        Assert.Throws<InvalidOperationException>(() => ZvecSet<TwoConventionKeys>.Create(NewDir()));
    }

    public class NullableKeyEntity
    {
        public int? Id { get; set; }

        [VectorField(Dimension = 2)] public float[]? Emb { get; set; }
    }

    [Fact]
    public void NullableKeyThrowsReadableError()
    {
        NotSupportedException ex =
            Assert.Throws<NotSupportedException>(() => ZvecSet<NullableKeyEntity>.Create(NewDir()));
        Assert.Contains("主键不应可空", ex.Message);
    }

    [Fact]
    public void EmptyStringKeyRejectedOnUpsert()
    {
        using ZvecSet<Product> set = ZvecSet<Product>.Create(NewDir());
        Assert.Throws<ArgumentException>(() => set.Upsert(
            new Product { Sku = "", Name = "n", Price = 1, Stock = 1, Embedding = [1, 0, 0, 0] }));
    }

    public class BadSparseEntity
    {
        public int Id { get; set; }

        [VectorField(Dimension = 300)]
        public SparseVector? Keywords { get; set; }
    }

    [Fact]
    public void SparseVectorWithDimensionThrows()
    {
        Assert.Throws<NotSupportedException>(() => ZvecSet<BadSparseEntity>.Create(NewDir()));
    }

    [Fact]
    public async Task AsyncUpsertWritesAndFindSimilarCancellationFailsFast()
    {
        using ZvecSet<Product> set = ZvecSet<Product>.Create(NewDir());
        Assert.True((await set.UpsertAsync(
            new Product { Sku = "a1", Name = "n", Price = 1, Stock = 1, Embedding = [1, 0, 0, 0] })).Success);

        WriteResult[] results = await set.UpsertRangeAsync(
        [
            new Product { Sku = "a2", Name = "m", Price = 2, Stock = 2, Embedding = [0, 1, 0, 0] },
        ]);
        Assert.True(results[0].Success);
        Assert.Equal(2ul, set.Underlying.Stats.DocCount);

        using var context = new ShopContext();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => set.FindSimilarAsync(context, [1, 0, 0, 0], cancellationToken: cancelled.Token));
    }
}
