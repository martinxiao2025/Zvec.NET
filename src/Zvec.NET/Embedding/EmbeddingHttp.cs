using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Zvec.NET.Embedding;

/// <summary>
/// 嵌入服务 HTTP 客户端基类（OpenAI /v1/embeddings 兼容协议）。
/// 安全约束：仅允许 http/https；每次请求前解析目标 host，
/// 拒绝 localhost、环回、私有、链路本地、站点本地、唯一本地、多播与保留地址。
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
            _httpClient = new HttpClient { Timeout = timeout ?? TimeSpan.FromSeconds(30) };
            _ownsClient = true;
        }
    }

    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>发送前校验目标 URL：仅 http/https，且 host 解析结果不得命中本地/私有/保留网段。</summary>
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
