namespace RobloxAutoManager.Core.Utils;

using System.Net.Http;
using System.Net.Http.Headers;

public static class RobloxHttpClient
{
    private static readonly Lazy<HttpClient> _instance = new(() =>
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Brotli
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return client;
    });

    public static HttpClient Instance => _instance.Value;

    public static HttpRequestMessage CreateRequest(HttpMethod method, string url, string? cookie = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrWhiteSpace(cookie))
            req.Headers.TryAddWithoutValidation("Cookie", $".ROBLOSECURITY={cookie}");
        return req;
    }
}