using System.Net.Http;

namespace SwapMid.Infrastructure;

/// <summary>
/// 标记请求 URI 自带凭据，供宿主在创建 OpenTelemetry Activity 前整条排除。
/// 该标记必须在调用 <see cref="HttpClient.SendAsync(HttpRequestMessage, System.Threading.CancellationToken)"/> 前写入。
/// </summary>
public static class SensitiveOutboundRequestOptions
{
    private static readonly HttpRequestOptionsKey<bool> ContainsCredential =
        new("SwapMid.Outbound.ContainsCredential");

    public static void Mark(HttpRequestMessage request)
        => request.Options.Set(ContainsCredential, true);

    public static bool IsMarked(HttpRequestMessage request)
        => request.Options.TryGetValue(ContainsCredential, out var marked) && marked;
}
