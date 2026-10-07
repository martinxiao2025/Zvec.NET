using System.Text.Json;

namespace Zvec.NET.Embedding;

/// <summary>
/// OpenAI 兼容 /v1/embeddings 稠密嵌入客户端（对齐 Python OpenAIDenseEmbedding）。
/// 适用于 OpenAI 及一切兼容端点（vLLM、Azure OpenAI 代理、本地 Ollama 等）；默认端点 api.openai.com。
/// 注意：默认安全策略拒绝 localhost/私有地址的 base_url；本地部署端点需在构造时显式
/// 传入 allowLocalEndpoint: true 放行。
/// </summary>
public sealed class OpenAIEmbedding : EmbeddingHttpClientBase, IDenseEmbeddingFunction
{
    private readonly int? _dimension;

    /// <summary>请求的向量维度（若端点支持 dimensions 参数）；0 表示未指定。</summary>
    public int Dimension => _dimension ?? 0;

    /// <summary>构造 OpenAI 兼容嵌入客户端。</summary>
    /// <param name="model">模型名。</param>
    /// <param name="apiKey">API Key（Bearer）；null 表示匿名端点。</param>
    /// <param name="baseUrl">兼容端点基址。</param>
    /// <param name="dimension">目标维度（可选）。</param>
    /// <param name="httpClient">自定义 HttpClient（不传则自建并启用建连时刻安全校验）。</param>
    /// <param name="timeout">请求超时（默认 30 秒）。</param>
    /// <param name="allowLocalEndpoint">放行 localhost/环回/私有/保留地址端点（本地 Ollama、vLLM 等）；
    /// 默认 false（安全策略拒绝）。</param>
    public OpenAIEmbedding(string model = "text-embedding-3-small", string? apiKey = null,
        string baseUrl = "https://api.openai.com/v1", int? dimension = null,
        HttpClient? httpClient = null, TimeSpan? timeout = null, bool allowLocalEndpoint = false)
        : base(baseUrl, model, apiKey, httpClient, timeout, allowLocalEndpoint)
    {
        _dimension = dimension;
    }

    /// <summary>编码单个文本（同步，经 Task.Run 脱离调用方同步上下文）。</summary>
    /// <param name="input">输入文本。</param>
    public float[] Embed(string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);
        return EmbedBatch([input])[0];
    }

    /// <summary>批量编码：返回与输入顺序一一对应的向量数组（响应 index 越界或缺失条目会抛出异常）。</summary>
    /// <param name="inputs">输入文本列表。</param>
    public float[][] EmbedBatch(IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return [];
        }

        // 同步上下文使用：嵌入调用本身为远程 IO，同步等待与 Python SDK 行为一致。
        using JsonDocument response = PostJsonSync("/embeddings", BuildPayload(inputs));
        return ParseBatchEmbeddings(response, inputs.Count);
    }

    /// <summary>异步批量编码：返回与输入顺序一一对应的向量数组。</summary>
    /// <param name="inputs">输入文本列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return [];
        }

        using JsonDocument response = await PostJsonAsync("/embeddings", BuildPayload(inputs), cancellationToken).ConfigureAwait(false);
        return ParseBatchEmbeddings(response, inputs.Count);
    }

    /// <summary>异步编码单个文本。</summary>
    /// <param name="input">输入文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);

        using JsonDocument response = await PostJsonAsync(
            "/embeddings", BuildPayload([input]), cancellationToken).ConfigureAwait(false);
        return ParseEmbedding(ReadFirstEmbedding(response));
    }

    private Dictionary<string, object?> BuildPayload(IReadOnlyList<string> inputs)
    {
        var payload = new Dictionary<string, object?> { ["model"] = Model, ["input"] = inputs };
        if (_dimension is not null)
        {
            payload["dimensions"] = _dimension;
        }

        return payload;
    }
}

/// <summary>Qwen（DashScope 兼容模式）稠密嵌入（对齐 Python QwenDenseEmbedding）。</summary>
public sealed class QwenDenseEmbedding : EmbeddingHttpClientBase, IDenseEmbeddingFunction
{
    private readonly int? _dimension;

    /// <summary>构造 Qwen 嵌入客户端（DashScope 兼容端点）。</summary>
    /// <param name="dimension">向量维度。</param>
    /// <param name="apiKey">DashScope API Key（Bearer）。</param>
    /// <param name="model">模型名。</param>
    /// <param name="baseUrl">兼容端点基址。</param>
    /// <param name="textType">text_type 参数（query/document，可选）。</param>
    /// <param name="httpClient">自定义 HttpClient（不传则自建并启用建连时刻安全校验）。</param>
    /// <param name="timeout">请求超时（默认 30 秒）。</param>
    /// <param name="allowLocalEndpoint">放行 localhost/环回/私有/保留地址端点；默认 false（安全策略拒绝）。</param>
    public QwenDenseEmbedding(int dimension, string apiKey, string model = "text-embedding-v4",
        string baseUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1",
        string? textType = null, HttpClient? httpClient = null, TimeSpan? timeout = null,
        bool allowLocalEndpoint = false)
        : base(baseUrl, model, apiKey, httpClient, timeout, allowLocalEndpoint)
    {
        _dimension = dimension;
        TextType = textType;
    }

    /// <summary>text_type 参数（query/document）；null 表示不传。</summary>
    public string? TextType { get; }

    /// <summary>请求的向量维度；0 表示未指定。</summary>
    public int Dimension => _dimension ?? 0;

    /// <summary>编码单个文本（同步，经 Task.Run 脱离调用方同步上下文）。</summary>
    /// <param name="input">输入文本。</param>
    public float[] Embed(string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);
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

        using JsonDocument response = PostJsonSync("/embeddings", payload);
        return ParseEmbedding(ReadFirstEmbedding(response));
    }

    /// <summary>异步编码单个文本。</summary>
    /// <param name="input">输入文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);
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

        using JsonDocument response = await PostJsonAsync("/embeddings", payload, cancellationToken).ConfigureAwait(false);
        return ParseEmbedding(ReadFirstEmbedding(response));
    }
}

/// <summary>Jina 稠密嵌入（对齐 Python JinaDenseEmbedding；Jina /v1/embeddings 兼容 OpenAI 协议）。</summary>
public sealed class JinaEmbedding : EmbeddingHttpClientBase, IDenseEmbeddingFunction
{
    private readonly int? _dimension;

    /// <summary>构造 Jina 嵌入客户端。</summary>
    /// <param name="apiKey">Jina API Key（Bearer）。</param>
    /// <param name="model">模型名。</param>
    /// <param name="dimension">目标维度（可选）。</param>
    /// <param name="baseUrl">兼容端点基址。</param>
    /// <param name="httpClient">自定义 HttpClient（不传则自建并启用建连时刻安全校验）。</param>
    /// <param name="timeout">请求超时（默认 30 秒）。</param>
    /// <param name="allowLocalEndpoint">放行 localhost/环回/私有/保留地址端点；默认 false（安全策略拒绝）。</param>
    public JinaEmbedding(string apiKey, string model = "jina-embeddings-v3", int? dimension = null,
        string baseUrl = "https://api.jina.ai/v1", HttpClient? httpClient = null, TimeSpan? timeout = null,
        bool allowLocalEndpoint = false)
        : base(baseUrl, model, apiKey, httpClient, timeout, allowLocalEndpoint)
    {
        _dimension = dimension;
    }

    /// <summary>请求的向量维度；0 表示未指定。</summary>
    public int Dimension => _dimension ?? 0;

    /// <summary>编码单个文本（同步，经 Task.Run 脱离调用方同步上下文）。</summary>
    /// <param name="input">输入文本。</param>
    public float[] Embed(string input)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["input"] = new[] { input },
        };
        if (_dimension is not null)
        {
            payload["dimensions"] = _dimension;
        }

        using JsonDocument response = PostJsonSync("/embeddings", payload);
        return ParseEmbedding(ReadFirstEmbedding(response));
    }

    /// <summary>异步编码单个文本。</summary>
    /// <param name="input">输入文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<float[]> EmbedAsync(string input, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(input);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["input"] = new[] { input },
        };
        if (_dimension is not null)
        {
            payload["dimensions"] = _dimension;
        }

        using JsonDocument response = await PostJsonAsync("/embeddings", payload, cancellationToken).ConfigureAwait(false);
        return ParseEmbedding(ReadFirstEmbedding(response));
    }
}
