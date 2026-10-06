using System.Text.Json;

namespace Zvec.NET.Embedding;

/// <summary>
/// OpenAI 兼容 /v1/embeddings 稠密嵌入客户端（对齐 Python OpenAIDenseEmbedding）。
/// 适用于 OpenAI 及一切兼容端点（vLLM、Azure OpenAI 代理等）；默认端点 api.openai.com。
/// 注意：出于安全策略，不支持指向 localhost/私有地址的 base_url。
/// </summary>
public sealed class OpenAIEmbedding : EmbeddingHttpClientBase, IDenseEmbeddingFunction
{
    private readonly int? _dimension;

    public int Dimension => _dimension ?? 0;

    public OpenAIEmbedding(string model = "text-embedding-3-small", string? apiKey = null,
        string baseUrl = "https://api.openai.com/v1", int? dimension = null,
        HttpClient? httpClient = null, TimeSpan? timeout = null)
        : base(baseUrl, model, apiKey, httpClient, timeout)
    {
        _dimension = dimension;
    }

    public float[] Embed(string input) => EmbedBatch([input])[0];

    /// <summary>批量编码：返回与输入顺序一一对应的向量数组。</summary>
    public float[][] EmbedBatch(IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return [];
        }

        var payload = new Dictionary<string, object?> { ["model"] = Model, ["input"] = inputs };
        if (_dimension is not null)
        {
            payload["dimensions"] = _dimension;
        }

        // 同步上下文使用：嵌入调用本身为远程 IO，同步等待与 Python SDK 行为一致。
        JsonDocument response = PostJsonAsync("/embeddings", payload, CancellationToken.None)
            .GetAwaiter().GetResult();
        using (response)
        {
            float[][] vectors = new float[inputs.Count][];
            foreach (JsonElement item in response.RootElement.GetProperty("data").EnumerateArray())
            {
                int index = item.GetProperty("index").GetInt32();
                vectors[index] = ParseEmbedding(item.GetProperty("embedding"));
            }

            return vectors;
        }
    }

    /// <summary>异步编码单个文本。</summary>
    public async Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?> { ["model"] = Model, ["input"] = new[] { input } };
        if (_dimension is not null)
        {
            payload["dimensions"] = _dimension;
        }

        using JsonDocument response = await PostJsonAsync("/embeddings", payload, cancellationToken).ConfigureAwait(false);
        return ParseEmbedding(response.RootElement.GetProperty("data")[0].GetProperty("embedding"));
    }
}

/// <summary>Qwen（DashScope 兼容模式）稠密嵌入（对齐 Python QwenDenseEmbedding）。</summary>
public sealed class QwenDenseEmbedding : EmbeddingHttpClientBase, IDenseEmbeddingFunction
{
    private readonly int? _dimension;

    public QwenDenseEmbedding(int dimension, string apiKey, string model = "text-embedding-v4",
        string baseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
        string? textType = null, HttpClient? httpClient = null, TimeSpan? timeout = null)
        : base(baseUrl, model, apiKey, httpClient, timeout)
    {
        _dimension = dimension;
        TextType = textType;
    }

    public string? TextType { get; }

    public int Dimension => _dimension ?? 0;

    public float[] Embed(string input)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["input"] = input,
            ["dimensions"] = _dimension,
        };
        if (TextType is not null)
        {
            payload["text_type"] = TextType;
        }

        JsonDocument response = PostJsonAsync("/embeddings", payload, CancellationToken.None)
            .GetAwaiter().GetResult();
        using (response)
        {
            return ParseEmbedding(response.RootElement.GetProperty("data")[0].GetProperty("embedding"));
        }
    }
}

/// <summary>Jina 稠密嵌入（对齐 Python JinaDenseEmbedding；Jina /v1/embeddings 兼容 OpenAI 协议）。</summary>
public sealed class JinaEmbedding : EmbeddingHttpClientBase, IDenseEmbeddingFunction
{
    private readonly int? _dimension;

    public JinaEmbedding(string apiKey, string model = "jina-embeddings-v3", int? dimension = null,
        string baseUrl = "https://api.jina.ai/v1", HttpClient? httpClient = null, TimeSpan? timeout = null)
        : base(baseUrl, model, apiKey, httpClient, timeout)
    {
        _dimension = dimension;
    }

    public int Dimension => _dimension ?? 0;

    public float[] Embed(string input)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["input"] = new[] { input },
        };
        if (_dimension is not null)
        {
            payload["dimensions"] = _dimension;
        }

        JsonDocument response = PostJsonAsync("/embeddings", payload, CancellationToken.None)
            .GetAwaiter().GetResult();
        using (response)
        {
            return ParseEmbedding(response.RootElement.GetProperty("data")[0].GetProperty("embedding"));
        }
    }
}
