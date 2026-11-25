namespace RobloxAutoManager.Watcher.Http;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RobloxAutoManager.Core.Models;
using RobloxAutoManager.Core.Services;
using RobloxAutoManager.Watcher.Resilience;

public sealed class RobloxApiPipeline
{
    private readonly HttpClient _http;
    private readonly IRateLimiter _limiter;
    private readonly ILogService _logs;
    private readonly ILogger<RobloxApiPipeline> _logger;
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuits = new(StringComparer.OrdinalIgnoreCase);
    private readonly RetryOptions _retry;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public RobloxApiPipeline(
        HttpClient http,
        IRateLimiter limiter,
        ILogService logs,
        ILogger<RobloxApiPipeline> logger,
        RetryOptions? retry = null)
    {
        _http = http;
        _limiter = limiter;
        _logs = logs;
        _logger = logger;
        _retry = retry ?? new RetryOptions();
    }

    /// Bucket key: OAuth'ta kota access-token başına olduğu için kullanıcı bazlı ayırıyoruz.
    public static string BucketFor(string host, string? username)
        => string.IsNullOrEmpty(username) ? host : $"{host}|{username}";

    public async Task<ApiRequestOutcome<T>> SendAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        string? username = null,
        Action<HttpResponseHeaders>? onHeaders = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int attempt = 0;
        RateLimitSnapshot? lastSnapshot = null;
        string bucketKey = "unknown";

        while (attempt < _retry.MaxAttempts)
        {
            attempt++;
            ct.ThrowIfCancellationRequested();

            using var request = requestFactory();
            var host = request.RequestUri?.Host ?? "unknown";
            bucketKey = BucketFor(host, username);

            var circuit = _circuits.GetOrAdd(bucketKey,
                k => new CircuitBreaker(k, _logger));

            if (!circuit.AllowRequest())
            {
                var wait = circuit.RemainingOpenTime;
                _logs.Log(LogLevel.Warning, "ApiPipeline",
                    $"Circuit open, rejecting request ({wait.TotalSeconds:F0}s left)", username, request.RequestUri?.ToString());
                return ApiRequestOutcome<T>.Fail(0, ApiFailureKind.CircuitOpen,
                    $"Circuit open for {bucketKey}", attempt, sw.Elapsed, lastSnapshot);
            }

            IDisposable? lease = null;
            HttpResponseMessage? response = null;

            try
            {
                lease = await _limiter.AcquireAsync(bucketKey, ct);
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                _limiter.ObserveHeaders(bucketKey, response.Headers);
                lastSnapshot = _limiter.GetSnapshot(bucketKey);
                onHeaders?.Invoke(response.Headers);

                if (response.IsSuccessStatusCode)
                {
                    circuit.RecordSuccess();

                    T? value = default;
                    if (typeof(T) != typeof(Unit) && response.Content.Headers.ContentLength != 0)
                        value = await response.Content.ReadFromJsonAsync<T>(JsonOpts, ct);

                    _logs.Log(LogLevel.Debug, "ApiPipeline",
                        $"{request.Method} {request.RequestUri?.AbsolutePath} → {(int)response.StatusCode} (attempt {attempt})",
                        username);

                    return ApiRequestOutcome<T>.Ok(value!, (int)response.StatusCode, attempt, sw.Elapsed, lastSnapshot);
                }

                var kind = RetryPolicy.Classify(response.StatusCode);
                string body = await SafeReadBodyAsync(response, ct);

                if (kind == ApiFailureKind.RateLimited)
                {
                    var delay = RetryPolicy.ComputeDelay(attempt, _retry, response.Headers, lastSnapshot);
                    _limiter.Penalize(bucketKey, delay);
                    circuit.RecordFailure();

                    _logs.Log(LogLevel.Warning, "ApiPipeline",
                        $"429 rate limited. Backing off {delay.TotalSeconds:F1}s (attempt {attempt}/{_retry.MaxAttempts})",
                        username, body);

                    if (attempt >= _retry.MaxAttempts)
                        return ApiRequestOutcome<T>.Fail((int)response.StatusCode, kind, body, attempt, sw.Elapsed, lastSnapshot);

                    await Task.Delay(delay, ct);
                    continue;
                }

                if (RetryPolicy.IsRetryable(kind))
                {
                    circuit.RecordFailure();
                    var delay = RetryPolicy.ComputeDelay(attempt, _retry, response.Headers, lastSnapshot);

                    _logs.Log(LogLevel.Warning, "ApiPipeline",
                        $"{(int)response.StatusCode} transient. Retry in {delay.TotalSeconds:F1}s", username, body);

                    if (attempt >= _retry.MaxAttempts)
                        return ApiRequestOutcome<T>.Fail((int)response.StatusCode, kind, body, attempt, sw.Elapsed, lastSnapshot);

                    await Task.Delay(delay, ct);
                    continue;
                }

                // 401/403/4xx — retry anlamsız, hemen dön
                circuit.RecordSuccess(); // sunucu sağlıklı, sorun auth/istek
                _logs.Log(LogLevel.Error, "ApiPipeline",
                    $"{(int)response.StatusCode} non-retryable", username, body);

                return ApiRequestOutcome<T>.Fail((int)response.StatusCode, kind, body, attempt, sw.Elapsed, lastSnapshot);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return ApiRequestOutcome<T>.Fail(0, ApiFailureKind.Cancelled, "Cancelled", attempt, sw.Elapsed, lastSnapshot);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                circuit.RecordFailure();
                var delay = RetryPolicy.ComputeDelay(attempt, _retry, null, lastSnapshot);

                _logs.Log(LogLevel.Warning, "ApiPipeline",
                    $"Network fault: {ex.GetType().Name}. Retry in {delay.TotalSeconds:F1}s", username, ex.Message);

                if (attempt >= _retry.MaxAttempts)
                    return ApiRequestOutcome<T>.Fail(0, ApiFailureKind.TransientNetwork, ex.Message, attempt, sw.Elapsed, lastSnapshot);

                await Task.Delay(delay, ct);
            }
            finally
            {
                response?.Dispose();
                lease?.Dispose();
            }
        }

        return ApiRequestOutcome<T>.Fail(0, ApiFailureKind.Unknown, "Retry budget exhausted", attempt, sw.Elapsed, lastSnapshot);
    }

    public Task<ApiRequestOutcome<Unit>> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        string? username = null,
        Action<HttpResponseHeaders>? onHeaders = null,
        CancellationToken ct = default)
        => SendAsync<Unit>(requestFactory, username, onHeaders, ct);

    public IReadOnlyDictionary<string, RateLimitSnapshot> SnapshotAll() => _limiter.GetAllSnapshots();

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            string text = await resp.Content.ReadAsStringAsync(ct);
            return text.Length > 2000 ? text[..2000] + "…[truncated]" : text;
        }
        catch { return "<unreadable>"; }
    }
}

public readonly record struct Unit;