using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using Zvec.NET;
using Zvec.NET.Embedding;

namespace Zvec.NET.Tests;

/// <summary>本地 Ollama 可用性探测（进程内缓存一次）；未安装/未启动或无 bge-m3 时测试跳过。</summary>
internal static class OllamaProbe
{
    public const string BaseUrl = "http://localhost:11434";
    public const string Model = "bge-m3";

    private static readonly Lazy<bool> AvailableLazy = new(Probe);

    public static bool IsAvailable => AvailableLazy.Value;

    private static bool Probe()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using JsonDocument doc = JsonDocument.Parse(
                client.GetStringAsync(BaseUrl + "/api/tags").GetAwaiter().GetResult());
            foreach (JsonElement model in doc.RootElement.GetProperty("models").EnumerateArray())
            {
                if (model.GetProperty("name").GetString()?.StartsWith(Model, StringComparison.OrdinalIgnoreCase) == true)
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

/// <summary>allowLocalEndpoint 显式放行的 URL 校验行为（无需 Ollama 在线）。</summary>
public sealed class LocalEndpointPolicyTests
{
    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:11434/v1")]
    [InlineData("http://[::1]:11434/v1")]
    [InlineData("http://192.168.1.10:11434/v1")]
    [InlineData("http://10.0.0.5:8080/v1")]
    public async Task AllowLocalEndpointSkipsAddressChecks(string url) =>
        await EmbeddingHttpClientBase.ValidateRequestUriAsync(new Uri(url), allowLocalEndpoint: true);

    [Theory]
    [InlineData("file://localhost/v1")]
    [InlineData("ftp://127.0.0.1/v1")]
    public async Task AllowLocalEndpointStillEnforcesHttpScheme(string url) =>
        await Assert.ThrowsAsync<NotSupportedException>(
            () => EmbeddingHttpClientBase.ValidateRequestUriAsync(new Uri(url), allowLocalEndpoint: true));

    [Fact]
    public async Task DefaultPolicyStillRejectsLocalhost()
    {
        // 回归保障：不传 allowLocalEndpoint 时安全策略照旧生效。
        await Assert.ThrowsAsync<NotSupportedException>(
            () => EmbeddingHttpClientBase.ValidateRequestUriAsync(new Uri("http://localhost:11434/v1")));
        using var embedding = new OpenAIEmbedding(baseUrl: "http://localhost:11434/v1");
        Assert.Throws<NotSupportedException>(() => embedding.Embed("hi"));
    }
}

/// <summary>本地 Ollama（bge-m3，1024 维）经库内 OpenAIEmbedding 客户端到 Zvec 检索的全链路验证。
/// 依赖本机 Ollama 服务与 bge-m3 模型：可用 <c>dotnet test --filter Category=RequiresOllama</c> 单独运行；
/// 不可用时自动跳过，保证无 Ollama 环境的套件保持绿色。</summary>
[Trait("Category", "RequiresOllama")]
public sealed class OllamaBgeM3EndToEndTests : IClassFixture<ZvecFixture>
{
    private const int Dimension = 1024;

    private static readonly (string Id, string Group, string Text)[] Corpus =
    [
        ("vec1", "vecdb", "Zvec 是一个嵌入式向量数据库，支持高性能向量检索"),
        ("vec2", "vecdb", "向量数据库用于存储和检索文本的嵌入向量"),
        ("vec3", "vecdb", "相似度搜索通常使用余弦距离或内积作为度量"),
        ("food1", "food", "今晚吃了一碗热腾腾的牛肉拉面"),
        ("food2", "food", "红烧肉的做法需要先炒糖色再慢炖"),
        ("sport1", "sport", "足球比赛在大雨中进行，双方战成平局"),
    ];

    private readonly ZvecFixture _fixture;
    private readonly ITestOutputHelper _output;

    public OllamaBgeM3EndToEndTests(ZvecFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private static OpenAIEmbedding CreateEmbedding(HttpClient? httpClient = null) =>
        new(model: OllamaProbe.Model, baseUrl: OllamaProbe.BaseUrl + "/v1",
            httpClient: httpClient, timeout: TimeSpan.FromMinutes(2), allowLocalEndpoint: true);

    private static float Cosine(float[] a, float[] b)
    {
        float dot = 0, na = 0, nb = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }

        return dot / (MathF.Sqrt(na) * MathF.Sqrt(nb));
    }

    [Fact]
    public void EmbedSingleTextReturnsValidVector()
    {
        if (!OllamaProbe.IsAvailable)
        {
            _output.WriteLine("跳过：本地 Ollama/bge-m3 不可用。");
            return;
        }

        using OpenAIEmbedding embedding = CreateEmbedding();
        float[] vector = embedding.Embed("你好，向量数据库");

        Assert.Equal(Dimension, vector.Length);
        Assert.All(vector, v => Assert.True(float.IsFinite(v)));
        Assert.True(MathF.Sqrt(vector.Sum(v => v * v)) > 1e-6f, "嵌入向量不应为零向量。");
    }

    [Fact]
    public async Task AsyncAndExternalHttpClientPathsAgree()
    {
        if (!OllamaProbe.IsAvailable)
        {
            _output.WriteLine("跳过：本地 Ollama/bge-m3 不可用。");
            return;
        }

        // 自建客户端（放行后走默认建连）与外部 HttpClient（走发送前校验分支）两条路径。
        using OpenAIEmbedding selfBuilt = CreateEmbedding();
        using var external = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using OpenAIEmbedding viaExternal = CreateEmbedding(external);

        float[] single = await selfBuilt.EmbedAsync("异步与批量路径一致性");
        float[][] batch = viaExternal.EmbedBatch(["批量第一条", "批量第二条", "异步与批量路径一致性"]);

        Assert.Equal(3, batch.Length);
        Assert.All(batch, v => Assert.Equal(Dimension, v.Length));
        float similarity = Cosine(single, batch[2]);
        Assert.True(similarity > 0.999f, $"同一文本的嵌入应一致，实际余弦相似度 {similarity}。");
    }

    [Fact]
    public void EndToEndSemanticSearchRanksCorrectGroup()
    {
        if (!OllamaProbe.IsAvailable)
        {
            _output.WriteLine("跳过：本地 Ollama/bge-m3 不可用。");
            return;
        }

        using OpenAIEmbedding embedding = CreateEmbedding();

        float[][] vectors = embedding.EmbedBatch([.. Corpus.Select(c => c.Text)]);
        Assert.All(vectors, v => Assert.Equal(Dimension, v.Length));
        float[] query = embedding.Embed("如何用向量数据库做文本相似度检索");

        // 嵌入质量前置校验：查询与全部同组文本的余弦相似度应高于任何跨组文本。
        Dictionary<string, float[]> byId = Corpus
            .Select((c, i) => (c.Id, c.Group, Vector: vectors[i]))
            .ToDictionary(x => x.Id, x => x.Vector);
        float minSameGroup = Corpus.Where(c => c.Group == "vecdb")
            .Select(c => Cosine(query, byId[c.Id])).Min();
        float maxCrossGroup = Corpus.Where(c => c.Group != "vecdb")
            .Select(c => Cosine(query, byId[c.Id])).Max();
        Assert.True(minSameGroup > maxCrossGroup,
            $"嵌入区分度不足：同组最小 {minSameGroup:F4}，跨组最大 {maxCrossGroup:F4}。");

        using Collection collection = Zvec.CreateAndOpen(_fixture.NewDir(),
            new CollectionSchema("ollama_e2e")
                .AddField(new FieldSchema("text", DataType.String))
                .AddVector(new VectorSchema("emb", DataType.VectorFp32, Dimension)));

        collection.Insert(Corpus.Select((c, i) => new Doc(c.Id,
            fields: new() { ["text"] = c.Text },
            vectors: new() { ["emb"] = vectors[i] })));
        collection.Flush();
        Assert.Equal((ulong)Corpus.Length, collection.Stats.DocCount);

        IReadOnlyList<Doc> results = collection.Query(new Query("emb", vector: query), topk: 6);

        Assert.Equal(Corpus.Length, results.Count);
        Assert.All(results, r => Assert.True(r.Score.HasValue));
        foreach (Doc doc in results.Take(3))
        {
            _output.WriteLine($"{doc.Id}  score={doc.Score:F4}  text={doc.Field("text")}");
        }

        // 检索语义验证：Top1 必属 vecdb 组，且前三名中至少两席属于该组。
        Assert.Equal("vecdb", Corpus.Single(c => c.Id == results[0].Id).Group);
        Assert.True(results.Take(3).Count(r => Corpus.Single(c => c.Id == r.Id).Group == "vecdb") >= 2,
            "前三名应至少包含两个向量库主题文档。");

        // 引擎排序须与嵌入模型自身的相似度排序一致：引擎 Top1 应在客户端余弦 Top2 之内。
        string[] clientRanking = [.. Corpus
            .Select(c => (c.Id, Sim: Cosine(query, byId[c.Id])))
            .OrderByDescending(x => x.Sim)
            .Select(x => x.Id)];
        Assert.Contains(results[0].Id, clientRanking.Take(2));
    }
}
