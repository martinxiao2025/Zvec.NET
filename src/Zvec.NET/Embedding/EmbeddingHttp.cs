using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Zvec.NET.Embedding;

/// <summary>
/// 嵌入服务 HTTP 客户端基类（OpenAI /v1/embeddings 兼容协议）。
/// 安全约束：仅允许 http/https；目标 host 不得为 localhost、环回、私有、CGNAT、
/// 链路本地、站点本地、唯一本地、多播与保留地址。
/// 内置客户端的所有 TCP 连接经 <see cref="SocketsHttpHandler.ConnectCallback"/> 完成
/// 「解析 → 校验 → 直连校验通过的地址」，校验与连接针对同一次解析结果，
/// 消除 DNS 重绑定窗口（注入 HttpClient 时仅保留请求前预检）。
/// </summary>
public abstract class EmbeddingHttpClientBase : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    protected string BaseUrl { get; }

    protected string Model { get; }

    protected string? ApiKey { get; }

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
        }
        else
        {
            _httpClient = CreateSecureClient(timeout);
            _ownsClient = true;
        }
    }

    /// <summary>内置客户端：连接级安全校验 + 用户超时。</summary>
    private static HttpClient CreateSecureClient(TimeSpan? timeout)
    {
        TimeSpan effective = timeout ?? TimeSpan.FromSeconds(30);
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = effective,
            ConnectCallback = ConnectToValidatedAddressAsync,
        };
        return new HttpClient(handler) { Timeout = effective };
    }

    /// <summary>
    /// 每次建连时解析、校验并直连校验通过的地址（TLS 的 SNI/证书校验仍用原始主机名）。
    /// 由此重定向、重试、重建连接均无法绕过网段校验。
    /// </summary>
    private static async ValueTask<Stream> ConnectToValidatedAddressAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        string host = context.DnsEndPoint.Host;
        int port = context.DnsEndPoint.Port;

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out IPAddress? literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException($"无法解析嵌入服务地址 {host}：{ex.Message}", ex);
            }
        }

        if (addresses.Length == 0)
        {
            throw new InvalidOperationException($"嵌入服务地址 {host} 解析结果为空。");
        }

        foreach (IPAddress address in addresses)
        {
            if (IsDisallowedAddress(address))
            {
                throw new NotSupportedException(
                    $"嵌入服务地址 {host} 解析到本地/私有/保留地址 {address}，已被安全策略拒绝。");
            }
        }

        Exception? lastError = null;
        foreach (IPAddress address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                lastError = ex;
            }
        }

        throw lastError ?? new InvalidOperationException($"无法连接嵌入服务 {host}:{port}。");
    }

    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>发送前预检目标 URL：仅 http/https，且 host 解析结果不得命中本地/私有/保留网段。</summary>
    internal static void ValidateRequestUri(Uri uri)
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
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4(); // ::ffff:x.x.x.x 形式按 IPv4 网段校验，防绕过。
        }

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
                or >= 0x64400000 and <= 0x647FFFFF               // 100.64.0.0/10 CGNAT
                or 0x00000000                                      // 0.0.0.0 未指定
                or >= 0xE0000000 and <= 0xEFFFFFFF                // 224.0.0.0/4 多播
                or >= 0xF0000000;                                  // 240.0.0.0/4 保留
        }

        return false;
    }

    /// <summary>POST JSON 并解析响应；发送前执行 URL 安全校验。</summary>
    protected async Task<JsonDocument> PostJsonAsync(string path, object payload, CancellationToken cancellationToken)
    {
        Uri uri = new(BaseUrl + path);
        ValidateRequestUri(uri);

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

    protected static float[] ParseEmbedding(JsonElement element)
    {
        return [.. element.EnumerateArray().Select(item => item.GetSingle())];
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "...";

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}
