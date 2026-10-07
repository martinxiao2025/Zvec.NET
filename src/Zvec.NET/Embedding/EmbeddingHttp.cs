using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Zvec.NET.Embedding;

/// <summary>
/// 嵌入服务 HTTP 客户端基类（OpenAI /v1/embeddings 兼容协议）。
/// 安全约束：仅允许 http/https；目标 host 不得命中 localhost、环回、私有、链路本地、
/// 站点本地、唯一本地、多播与保留地址。
/// 自建 HttpClient（未传入 httpClient 参数）时校验发生在 SocketsHttpHandler.ConnectCallback
/// ——即 TCP 建连时刻对实际 IP 校验，消除"先解析校验、后发送再解析"的 DNS 重绑定窗口，
/// 且不产生额外的重复 DNS 查询；传入外部 HttpClient 时退回发送前校验（尽力而为）。
/// </summary>
public abstract class EmbeddingHttpClientBase : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    /// <summary>自有客户端具备建连时刻校验，发送前无需再做 DNS 解析校验。</summary>
    private readonly bool _connectTimeValidation;

    /// <summary>端点基址（无尾部斜杠）。</summary>
    protected string BaseUrl { get; }

    /// <summary>模型名。</summary>
    protected string Model { get; }

    /// <summary>API Key（Bearer）；null 表示匿名。</summary>
    protected string? ApiKey { get; }

    /// <summary>构造嵌入客户端基类。</summary>
    /// <param name="baseUrl">端点基址（仅 http/https，且不得指向内网/保留地址）。</param>
    /// <param name="model">模型名。</param>
    /// <param name="apiKey">API Key（Bearer）；null 表示匿名。</param>
    /// <param name="httpClient">自定义 HttpClient；不传则自建（含建连时刻安全校验与 5 分钟连接回收）。</param>
    /// <param name="timeout">请求超时（默认 30 秒，仅自建客户端生效）。</param>
    protected EmbeddingHttpClientBase(string baseUrl, string model, string? apiKey = null,
        HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseUrl);
        ArgumentException.ThrowIfNullOrEmpty(model);

        BaseUrl = baseUrl.TrimEnd('/');
        Model = model;
        ApiKey = apiKey;

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
                ConnectCallback = ConnectValidatedAsync,
                // 定期回收连接，避免长生命周期进程持有过期 DNS 的连接。
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            _httpClient = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
            _ownsClient = true;
            _connectTimeValidation = true;
        }
    }

    /// <summary>Web 风格（camelCase）的 JSON 序列化选项。</summary>
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>发送前校验目标 URL：仅 http/https，host 不得为 localhost 字面量。
    /// DNS 层校验视 <paramref name="validateDns"/> 而定（外部 HttpClient 无法接管建连，必须在此校验）。</summary>
    internal static void ValidateRequestUri(Uri uri, bool validateDns = true)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new NotSupportedException($"仅允许 http/https 协议，实际为 {uri.Scheme}。");
        }

        string host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "::1")
        {
            throw new NotSupportedException("禁止请求 localhost。");
        }

        if (validateDns)
        {
            ValidateHostAddresses(host);
        }
    }

    private static void ValidateHostAddresses(string host)
    {
        IPAddress[] addresses;
        try
        {
            addresses = Dns.GetHostAddresses(host);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"无法解析嵌入服务地址 {host}：{ex.Message}", ex);
        }

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
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal
            || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
        {
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = address.GetAddressBytes();
            uint first = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
            return first is >= 0x0A000000 and <= 0x0AFFFFFF      // 10.0.0.0/8
                or >= 0xAC100000 and <= 0xAC1FFFFF               // 172.16.0.0/12
                or >= 0xC0A80000 and <= 0xC0A8FFFF               // 192.168.0.0/16
                or >= 0xA9FE0000 and <= 0xA9FEFFFF               // 169.254.0.0/16 链路本地
                or 0x00000000                                      // 0.0.0.0 未指定
                or >= 0xE0000000 and <= 0xEFFFFFFF                // 224.0.0.0/4 多播
                or >= 0xF0000000;                                  // 240.0.0.0/4 保留
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
        string host = context.DnsEndPoint.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || host == "::1")
        {
            throw new NotSupportedException("禁止请求 localhost。");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException($"无法解析嵌入服务地址 {host}：{ex.Message}", ex);
        }

        foreach (IPAddress address in addresses)
        {
            if (IsDisallowedAddress(address))
            {
                throw new NotSupportedException(
                    $"嵌入服务地址 {host} 解析到本地/私有/保留地址 {address}，已被安全策略拒绝。");
            }
        }

        if (addresses.Length == 0)
        {
            throw new SocketException((int)SocketError.HostNotFound);
        }

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

    /// <summary>POST JSON 并解析响应；发送前执行 URL 安全校验（自有客户端跳过 DNS 重复校验）。</summary>
    /// <param name="path">相对路径（如 "/embeddings"）。</param>
    /// <param name="payload">请求体（将按 Web 风格序列化）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    protected async Task<JsonDocument> PostJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        Uri uri = new(BaseUrl + path);
        ValidateRequestUri(uri, validateDns: !_connectTimeValidation);

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
        };
        if (ApiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        }

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"嵌入服务返回 {(int)response.StatusCode}：{Truncate(body, 512)}", null, response.StatusCode);
        }

        return JsonDocument.Parse(body);
    }

    /// <summary>
    /// 同步等待嵌入请求：经 Task.Run 脱离调用方的同步上下文，
    /// 避免在 WinForms/WPF 等单线程同步上下文宿主中 sync-over-async 死锁。
    /// </summary>
    /// <param name="path">相对路径（如 "/embeddings"）。</param>
    /// <param name="payload">请求体。</param>
    protected JsonDocument PostJsonSync(string path, object payload) =>
        Task.Run(() => PostJsonAsync(path, payload, CancellationToken.None)).GetAwaiter().GetResult();

    /// <summary>把 JSON 数组元素解析为 float[] 向量。</summary>
    /// <param name="element">embedding 字段的 JSON 数组。</param>
    protected static float[] ParseEmbedding(JsonElement element)
    {
        return [.. element.EnumerateArray().Select(item => item.GetSingle())];
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

        GC.SuppressFinalize(this);
    }
}
