namespace RobloxAutoManager.Core.Models;

public sealed record RateLimitWindow(int Limit, int WindowSeconds)
{
    public static RateLimitWindow Parse(string segment)
    {
        var parts = segment.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        int limit = int.TryParse(parts[0], out var l) ? l : 0;
        int window = 60;

        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].StartsWith("w=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(parts[i][2..], out var w))
                window = w;
        }

        return new RateLimitWindow(limit, window);
    }
}

public sealed record RateLimitSnapshot(
    string BucketKey,
    IReadOnlyList<RateLimitWindow> Windows,
    int Remaining,
    TimeSpan ResetIn,
    DateTimeOffset ObservedAt)
{
    public bool IsExhausted => Remaining <= 0;

    public double Pressure => Windows.Count == 0
        ? 0
        : 1.0 - Math.Clamp(Remaining / (double)Math.Max(1, Windows.Min(w => w.Limit)), 0, 1);

    public static RateLimitSnapshot FromHeaders(string bucketKey, System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        var windows = new List<RateLimitWindow>();
        if (headers.TryGetValues("x-ratelimit-limit", out var limitVals))
        {
            foreach (var raw in limitVals)
                foreach (var seg in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    windows.Add(RateLimitWindow.Parse(seg));
        }

        int remaining = int.MaxValue;
        if (headers.TryGetValues("x-ratelimit-remaining", out var remVals) &&
            int.TryParse(remVals.FirstOrDefault(), out var r))
            remaining = r;

        TimeSpan reset = TimeSpan.FromSeconds(60);
        if (headers.TryGetValues("x-ratelimit-reset", out var resetVals) &&
            double.TryParse(resetVals.FirstOrDefault(), System.Globalization.CultureInfo.InvariantCulture, out var secs))
            reset = TimeSpan.FromSeconds(Math.Max(0, secs));

        return new RateLimitSnapshot(bucketKey, windows, remaining, reset, DateTimeOffset.UtcNow);
    }
}