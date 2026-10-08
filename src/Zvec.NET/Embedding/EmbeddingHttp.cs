using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Zvec.NET.Embedding;

/// <summary>
/// 嵌入服务 HTTP 客户端基类（OpenAI /v1/embeddings 兼容协议）。
/// 安全约束：仅允许 http/https；默认目标 host 不得命中 localhost、环回、私有、链路本地、
/// 站点本地、唯一本地、多播与保留地址（含 IPv4-mapped、NAT64、6to4、Teredo 等 IPv4 承载形式）。
/// 本地部署场景（Ollama、vLLM、LM Studio 等）可显式传入
/// allowLocalEndpoint: true 放行上述地址（协议限制不变），由调用方自行确保端点可信。
/// 自建 HttpClient（未传入 httpClient 参数）且未放行本地端点时，校验发生在
/// SocketsHttpHandler.ConnectCallback——即 TCP 建连时刻对实际 IP 校验，消除"先解析校验、
/// 后发送再解析"的 DNS 重绑定窗口，且不产生额外的重复 DNS 查询；传入外部 HttpClient 时
/// 退回发送前校验（尽力而为）。
/// </summary>
public abstract class EmbeddingHttpClientBase : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    /// <summary>自有客户端具备建连时刻校验，发送前无需再做 DNS 解析校验。</summary>
    private readonly bool _connectTimeValidation;

    /// <summary>显式放行本地/内网/保留地址端点（本地部署的嵌入服务）。</summary>
    private readonly bool _allowLocalEndpoint;

    /// <summary>端点基址（无尾部斜杠）。</summary>
    protected string BaseUrl { get; }

    /// <summary>模型名。</summary>
    protected string Model { get; }

    /// <summary>API Key（Bearer）；null 表示匿名。</summary>
    protected string? ApiKey { get; }

    /// <summary>构造嵌入客户端基类。</summary>
    /// <param name="baseUrl">端点基址（仅 http/https）。</param>
    /// <param name="model">模型名。</param>
    /// <param name="apiKey">API Key（Bearer）；null 表示匿名。</param>
    /// <param name="httpClient">自定义 HttpClient；不传则自建（含建连时刻安全校验与 5 分钟连接回收）。
    /// 注意：外部客户端由调用方负责安全策略——必须禁用自动重定向（或对每个重定向目标执行等价校验），
    /// 否则 3xx 跳转可绕过本库的发送前校验。</param>
    /// <param name="timeout">请求超时（默认 30 秒，仅自建客户端生效）。</param>
    /// <param name="allowLocalEndpoint">放行 localhost/环回/私有/保留地址的端点，用于本地部署的
    /// 嵌入服务（Ollama、vLLM 等）；默认 false（安全策略拒绝）。放行时协议限制不变。</param>
    protected EmbeddingHttpClientBase(string baseUrl, string model, string? apiKey = null,
        HttpClient? httpClient = null, TimeSpan? timeout = null, bool allowLocalEndpoint = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);
        ArgumentException.ThrowIfNullOrEmpty(model);

        BaseUrl = baseUrl.TrimEnd('/');
        Model = model;
        ApiKey = apiKey;
        _allowLocalEndpoint = allowLocalEndpoint;

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
            _connectTimeValidation = false;
        }
        else
        {
            var handler = new SocketsHttpHandler
            {
                // 定期回收连接，避免长生命周期进程持有过期 DNS 的连接。
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                // 嵌入端点为固定直连服务：禁系统代理（代理会接管目标解析，使建连时刻校验失效，
                // 且内网代理会被默认策略拒绝导致不可用），禁自动重定向（3xx 视为错误快速失败，
                // 防止凭据经重定向泄漏到非预期目标）。
                UseProxy = false,
                AllowAutoRedirect = false,
            };
            if (!allowLocalEndpoint)
            {
                // 默认安全策略：建连时刻校验实际 IP；放行本地端点时改用默认建连逻辑。
                handler.ConnectCallback = ConnectValidatedAsync;
                _connectTimeValidation = true;
            }
            _httpClient = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
            _ownsClient = true;
        }
    }

    /// <summary>Web 风格（camelCase）的 JSON 序列化选项。</summary>
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>发送前校验目标 URL：仅 http/https；未放行本地端点时 host 不得为 localhost 字面量，
    /// 并按 <paramref name="validateDns"/> 异步解析校验全部 IP（外部 HttpClient 无法接管建连，必须在此校验）。</summary>
    internal static async Task ValidateRequestUriAsync(Uri uri, bool validateDns = true, bool allowLocalEndpoint = false)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new NotSupportedException($"仅允许 http/https 协议，实际为 {uri.Scheme}。");
        }

        if (allowLocalEndpoint)
        {
            // 本地部署（Ollama/vLLM 等）显式放行：跳过内网/保留地址检查，协议限制仍然生效。
            return;
        }

        ThrowIfLocalhostLiteral(uri.Host);

        if (validateDns)
        {
            await ValidateHostAddressesAsync(uri.Host).ConfigureAwait(false);
        }
    }

    private static void ThrowIfLocalhostLiteral(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "::1")
        {
            throw new NotSupportedException("禁止请求 localhost。");
        }
    }

    private static async Task ValidateHostAddressesAsync(string host)
    {
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"无法解析嵌入服务地址 {host}：{ex.Message}", ex);
        }

        ThrowIfAnyDisallowed(addresses, host);
    }

    private static void ThrowIfAnyDisallowed(IPAddress[] addresses, string host)
    {
        foreach (IPAddress address in addresses)
        {
            if (IsDisallowedAddress(address))
            {
                throw new NotSupportedException(
                    $"嵌入服务地址 {host} 解析到本地/私有/保留地址 {address}，已被安全策略拒绝。");
            }
        }
    }

    private static bool IsDisallowedAddress(IPAddress address)
    {
        // IPv4-mapped IPv6（::ffff:a.b.c.d）在双栈 socket 上实际连到 IPv4，必须还原后按 IPv4 规则校验，
        // 否则字面量 http://[::ffff:10.1.2.3]/ 可绕过私网拒绝策略。
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
        {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            uint first = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            return first is >= 0x0A000000 and <= 0x0AFFFFFF      // 10.0.0.0/8
                or >= 0xAC100000 and <= 0xAC1FFFFF               // 172.16.0.0/12
                or >= 0xC0A80000 and <= 0xC0A8FFFF               // 192.168.0.0/16
                or >= 0xA9FE0000 and <= 0xA9FEFFFF               // 169.254.0.0/16 链路本地
                or >= 0x64400000 and <= 0x647FFFFF               // 100.64.0.0/10 CGNAT/共享地址空间
                or >= 0xC6120000 and <= 0xC613FFFF               // 198.18.0.0/15 基准测试
                or 0x00000000                                      // 0.0.0.0 未指定
                or >= 0xE0000000 and <= 0xEFFFFFFF                // 224.0.0.0/4 多播
                or >= 0xF0000000;                                  // 240.0.0.0/4 保留
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            // 全零（::，IPv6 未指定地址）：Linux 内核按环回处理，等价 0.0.0.0，必须拦截。
            if (bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            {
                return true;
            }

            // NAT64（Well-Known 64:ff9b::/96 与 RFC 8215 本地 64:ff9b:1::/48）：末 4 字节即被转换的 IPv4。
            if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B
                && bytes[4] == 0 && (bytes[5] == 0 || bytes[5] == 1)
                && bytes.AsSpan(6, 6).IndexOfAnyExcept((byte)0) < 0)
            {
                return IsDisallowedAddress(new IPAddress(bytes[12..16]));
            }

            // 6to4（2002::/16）：第 2-5 字节为承载的 IPv4 地址，按 IPv4 规则递归校验。
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
            {
                return IsDisallowedAddress(new IPAddress(bytes[2..6]));
            }

            // 已废弃的 IPv4 兼容格式（::a.b.c.d，前 12 字节为零且非全零）：按内嵌 IPv4 递归校验。
            if (bytes.AsSpan(0, 12).IndexOfAnyExcept((byte)0) < 0)
            {
                return IsDisallowedAddress(new IPAddress(bytes[12..16]));
            }

            // Teredo（2001:0::/32）。
            return bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0 && bytes[3] == 0;
        }

        return false;
    }

    /// <summary>
    /// 建连时刻校验：对本次实际解析到的 IP 做安全检查后再建立 TCP 连接。
    /// 替代 handler 默认建连逻辑（不做 Happy Eyeballs 并发竞速，按解析顺序逐个尝试）。
    /// </summary>
    private static async ValueTask<Stream> ConnectValidatedAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ThrowIfLocalhostLiteral(context.DnsEndPoint.Host);

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"无法解析嵌入服务地址 {context.DnsEndPoint.Host}：{ex.Message}", ex);
        }

        ThrowIfAnyDisallowed(addresses, context.DnsEndPoint.Host);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>嵌入响应体大小上限：防御异常端点返回超大 body 打爆内存（合法大批量嵌入远小于该值）。</summary>
    private const int MaxResponseBodyBytes = 256 * 1024 * 1024;

    /// <summary>错误消息中携带的响应体前缀长度。</summary>
    private const int ErrorBodyPrefixBytes = 512;

    /// <summary>POST JSON 并解析响应；发送前执行 URL 安全校验（自有客户端跳过 DNS 重复校验）。</summary>
    /// <param name="path">相对路径（如 "/embeddings"）。</param>
    /// <param name="payload">请求体（将按 Web 风格序列化）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected async Task<JsonDocument> PostJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        Uri uri = new(BaseUrl + path);
        await ValidateRequestUriAsync(uri, validateDns: !_connectTimeValidation, allowLocalEndpoint: _allowLocalEndpoint)
            .ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        if (ApiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 自建客户端禁用自动重定向，3xx 同样走此分支；错误体只读前缀，超大 body 不占内存。
            string errorBody = await ReadBodyPrefixAsync(response.Content, ErrorBodyPrefixBytes, cancellationToken)
                .ConfigureAwait(false);
            throw new HttpRequestException(
                $"嵌入服务返回 {(int)response.StatusCode}：{Truncate(errorBody, ErrorBodyPrefixBytes)}",
                null, response.StatusCode);
        }

        string body = await ReadBodyCappedAsync(response.Content, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(body);
    }

    /// <summary>限量读取响应体（Content-Length 预检 + 流式累计校验），超限抛出而非 OOM；
    /// 跳过可选的 UTF-8 BOM（部分 Windows 网关会在 application/json 前附加 BOM）。</summary>
    private static async Task<string> ReadBodyCappedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long declaredLength && declaredLength > MaxResponseBodyBytes)
        {
            throw new HttpRequestException(
                $"嵌入服务响应体过大（{declaredLength:N0} 字节，上限 {MaxResponseBodyBytes:N0}）。");
        }

        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > MaxResponseBodyBytes)
            {
                throw new HttpRequestException($"嵌入服务响应体超过上限 {MaxResponseBodyBytes:N0} 字节。");
            }

            buffer.Write(chunk, 0, read);
        }

        return DecodeUtf8SkippingBom(buffer.GetBuffer(), (int)buffer.Length);
    }

    /// <summary>读取响应体开头若干字节即停（错误消息用），并跳过可选的 UTF-8 BOM。</summary>
    private static async Task<string> ReadBodyPrefixAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[maxBytes + 3];
        int total = 0;
        while (total < buffer.Length
            && await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false) is int read && read > 0)
        {
            total += read;
        }

        return DecodeUtf8SkippingBom(buffer, total);
    }

    private static string DecodeUtf8SkippingBom(byte[] raw, int length) =>
        length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF
            ? Encoding.UTF8.GetString(raw, 3, length - 3)
            : Encoding.UTF8.GetString(raw, 0, length);

    /// <summary>
    /// 同步等待嵌入请求：经 Task.Run 脱离调用方的同步上下文，
    /// 避免在 WinForms/WPF 等单线程同步上下文宿主中 sync-over-async 死锁。
    /// </summary>
    /// <param name="path">相对路径（如 "/embeddings"）。</param>
    /// <param name="payload">请求体。</param>
    protected JsonDocument PostJsonSync(string path, object payload) =>
        Task.Run(() => PostJsonAsync(path, payload, CancellationToken.None)).GetAwaiter().GetResult();

    /// <summary>把 JSON 数组元素解析为 float[] 向量；非数值元素报错带序号，便于定位兼容端点问题。</summary>
    /// <param name="element">embedding 字段的 JSON 数组。</param>
    protected static float[] ParseEmbedding(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"embedding 字段必须是数值数组，实际为 {element.ValueKind}。");
        }

        float[] values = new float[element.GetArrayLength()];
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException($"embedding[{index}] 不是数值（{item.ValueKind}）。");
            }

            values[index++] = item.GetSingle();
        }

        return values;
    }

    /// <summary>读取 /embeddings 响应的 data[0].embedding（单文本路径），缺失或为空时抛出带上下文的异常。</summary>
    protected static JsonElement ReadFirstEmbedding(JsonDocument response)
    {
        if (!response.RootElement.TryGetProperty("data", out JsonElement data)
            || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("嵌入服务响应缺少非空的 data 数组。");
        }

        return data[0].TryGetProperty("embedding", out JsonElement embedding) && embedding.ValueKind == JsonValueKind.Array
            ? embedding
            : throw new InvalidOperationException("嵌入服务响应的 data[0] 缺少 embedding 数组。");
    }

    /// <summary>批量解析 /embeddings 响应：校验 index 区间与结果完整性（缺失条目不得以 null 槽位漏出）；
    /// 兼容不返回 index 字段的端点（按枚举顺序兜底）。</summary>
    /// <param name="response">/embeddings 响应。</param>
    /// <param name="expectedCount">输入条数。</param>
    protected static float[][] ParseBatchEmbeddings(JsonDocument response, int expectedCount)
    {
        if (!response.RootElement.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("嵌入服务响应缺少 data 数组。");
        }

        float[][] vectors = new float[expectedCount][];
        int fallbackIndex = 0;
        foreach (JsonElement item in data.EnumerateArray())
        {
            int index = item.TryGetProperty("index", out JsonElement indexElement)
                && indexElement.ValueKind == JsonValueKind.Number
                    ? indexElement.GetInt32()
                    : fallbackIndex;
            fallbackIndex++;
            if ((uint)index >= (uint)expectedCount)
            {
                throw new InvalidOperationException(
                    $"嵌入服务返回的 index {index} 超出输入数量 {expectedCount}。");
            }

            if (!item.TryGetProperty("embedding", out JsonElement embedding) || embedding.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"嵌入服务响应 data[{index}] 缺少 embedding 数组。");
            }

            vectors[index] = ParseEmbedding(embedding);
        }

        for (int i = 0; i < vectors.Length; i++)
        {
            if (vectors[i] is null)
            {
                throw new InvalidOperationException(
                    $"嵌入服务响应缺少第 {i} 条输入的嵌入结果（期望 {expectedCount} 条）。");
            }
        }

        return vectors;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    /// <summary>释放自建的 HttpClient（外部传入的客户端归调用方所有）。</summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }

        // CA1816：#pragma 未豁免；基类为公共非密封类型，SuppressFinalize 让带终结器的派生类型无需重写 IDisposable。
        GC.SuppressFinalize(this);
    }
}
