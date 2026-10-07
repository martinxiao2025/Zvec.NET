using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;
using Zvec.NET.Embedding;

namespace Zvec.NET.Tests;

/// <summary>自定义 HttpContent：允许伪造 Content-Length（测试响应体上限预检路径）。</summary>
internal sealed class StubContent(byte[] payload, long? lengthOverride) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        await stream.WriteAsync(payload).ConfigureAwait(false);

    protected override bool TryComputeLength(out long length)
    {
        length = lengthOverride ?? payload.Length;
        return true;
    }
}

/// <summary>按预设委托应答的 HttpMessageHandler：捕获请求供断言，无需真实嵌入服务。</summary>
internal sealed class StubHandler : HttpMessageHandler
{
    private volatile Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    public void SetResponder(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(_responder(request));
    }
}

/// <summary>OpenAIEmbedding 协议解析与错误路径的 mock 测试（无网络依赖；与本地 Ollama 无关，CI 可跑）。</summary>
public sealed class EmbeddingClientMockTests : IDisposable
{
    private readonly StubHandler _handler = new(_ => Json(HttpStatusCode.OK, "{}"));
    private readonly HttpClient _httpClient;
    private readonly OpenAIEmbedding _embedding;

    public EmbeddingClientMockTests()
    {
        _httpClient = new HttpClient(_handler);
        // 外部 HttpClient + 放行本地端点：命中 StubHandler，不出网。
        _embedding = new OpenAIEmbedding(
            model: "stub-model", baseUrl: "http://localhost:9/v1",
            httpClient: _httpClient, allowLocalEndpoint: true);
    }

    public void Dispose()
    {
        _embedding.Dispose();   // 不拥有外部 HttpClient，不会误释放。
        _httpClient.Dispose();
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json, long? contentLength = null)
    {
        HttpContent content = new StubContent(Encoding.UTF8.GetBytes(json), contentLength)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        };
        return new HttpResponseMessage(code) { Content = content };
    }

    private static string EmbeddingsJson(params (int Index, float[] Vector)[] items) => JsonSerializer.Serialize(new
    {
        data = items.Select(item => new { index = item.Index, embedding = item.Vector }),
    });

    private void SetResponder(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _handler.SetResponder(responder);

    [Fact]
    public void EmbedBatchRestoresInputOrderFromShuffledIndexes()
    {
        SetResponder(_ => Json(HttpStatusCode.OK,
            EmbeddingsJson((1, [0.5f, 0.6f]), (0, [0.1f, 0.2f]))));

        float[][] vectors = _embedding.EmbedBatch(["a", "b"]);

        Assert.Equal(2, vectors.Length);
        Assert.Equal([0.1f, 0.2f], vectors[0]);
        Assert.Equal([0.5f, 0.6f], vectors[1]);
    }

    [Fact]
    public void EmbedBatchFallsBackToEnumeratedOrderWithoutIndexField()
    {
        string json = JsonSerializer.Serialize(new
        {
            data = new object[] { new { embedding = new float[] { 1 } }, new { embedding = new float[] { 2 } } },
        });
        SetResponder(_ => Json(HttpStatusCode.OK, json));

        float[][] vectors = _embedding.EmbedBatch(["a", "b"]);

        Assert.Equal(1f, vectors[0][0]);
        Assert.Equal(2f, vectors[1][0]);
    }

    [Fact]
    public void EmbedBatchRejectsIndexOutOfRange()
    {
        SetResponder(_ => Json(HttpStatusCode.OK, EmbeddingsJson((5, [0.1f]))));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => _embedding.EmbedBatch(["a", "b"]));
        Assert.Contains("index 5", ex.Message);
    }

    [Fact]
    public void EmbedBatchRejectsMissingEntries()
    {
        SetResponder(_ => Json(HttpStatusCode.OK, EmbeddingsJson((0, [0.1f]))));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => _embedding.EmbedBatch(["a", "b"]));
        Assert.Contains("第 1 条", ex.Message);
    }

    [Fact]
    public void EmbedBatchRejectsNonNumericElementWithPosition()
    {
        SetResponder(_ => Json(HttpStatusCode.OK,
            """{"data":[{"index":0,"embedding":[0.5,"x"]}]}"""));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => _embedding.EmbedBatch(["a"]));
        Assert.Contains("embedding[1]", ex.Message);
    }

    [Fact]
    public async Task EmbedAsyncRejectsEmptyDataArray()
    {
        SetResponder(_ => Json(HttpStatusCode.OK, """{"data":[]}"""));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _embedding.EmbedAsync("a"));
    }

    [Fact]
    public async Task ErrorResponseIncludesStatusAndBody()
    {
        SetResponder(_ => Json(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}"));

        HttpRequestException ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => _embedding.EmbedAsync("a"));
        Assert.Contains("500", ex.Message);
        Assert.Contains("boom", ex.Message);
    }

    [Fact]
    public async Task OversizedContentLengthRejectedBeforeReadingBody()
    {
        SetResponder(_ => Json(HttpStatusCode.OK, "{}", contentLength: 300_000_000));

        await Assert.ThrowsAsync<HttpRequestException>(() => _embedding.EmbedAsync("a"));
    }

    [Fact]
    public void EmptyBatchSendsNoRequest()
    {
        SetResponder(_ => Json(HttpStatusCode.OK, "{}"));

        Assert.Empty(_embedding.EmbedBatch([]));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task EmptyOrNullSingleInputRejectedLocally()
    {
        Assert.Throws<ArgumentException>(() => _embedding.Embed(""));
        string nullInput = null!;
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _embedding.EmbedAsync(nullInput));
    }

    [Fact]
    public async Task RequestPayloadCarriesModelInputsAndOptionalDimensions()
    {
        // 请求体在 PostJsonAsync 返回后即被释放，须在 handler 内捕获。
        string? capturedPayload = null;
        SetResponder(request =>
        {
            capturedPayload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(HttpStatusCode.OK, EmbeddingsJson((0, [0.1f])));
        });

        using var withDimension = new OpenAIEmbedding(
            model: "stub-model", baseUrl: "http://localhost:9/v1", dimension: 64,
            httpClient: _httpClient, allowLocalEndpoint: true);
        withDimension.EmbedBatch(["x"]);

        Assert.NotNull(capturedPayload);
        Assert.Contains("\"model\":\"stub-model\"", capturedPayload);
        Assert.Contains("\"dimensions\":64", capturedPayload);
        Assert.Contains("x", capturedPayload);
    }
}
