using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Demo;

/// <summary>
/// Captures the remote client IP from the circuit's initial <see cref="HttpContext"/> at component init,
/// keeping it in circuit scope for subsequent navigations and re-runs.
/// </summary>
public sealed class ClientIdentity(IHttpContextAccessor? httpContextAccessor = null)
{
    public string? Key { get; private set; } = ExtractIp(httpContextAccessor?.HttpContext);

    public string EffectiveKey => string.IsNullOrWhiteSpace(Key) ? "unknown" : Key;

    public void Capture(HttpContext? httpContext = null)
    {
        if (string.IsNullOrWhiteSpace(Key))
        {
            Key = ExtractIp(httpContext ?? httpContextAccessor?.HttpContext);
        }
    }

    /// <summary>Seeds an explicit client key (for deterministic tests).</summary>
    public void Seed(string clientKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientKey);
        Key = clientKey.Trim();
    }

    private static string? ExtractIp(HttpContext? context) =>
        context?.Connection.RemoteIpAddress?.ToString();
}

/// <summary>
/// Sliding 1-minute per-client rate limiter for starting investigations (including cached replays).
/// <c>StartsPerClientPerMinute &lt;= 0</c> disables limiting.
/// </summary>
public sealed class StartLimiter
{
    public const string RateLimitedMessage =
        "Too many investigations started from this connection — wait a minute.";

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Func<int> _getLimit;
    private readonly TimeProvider _time;
    private readonly object _sync = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _buckets = new(StringComparer.Ordinal);

    [ActivatorUtilitiesConstructor]
    public StartLimiter(IOptionsMonitor<DemoOptions> options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _getLimit = () => options.CurrentValue.StartsPerClientPerMinute;
        _time = time;
    }

    public StartLimiter(int startsPerClientPerMinute, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _getLimit = () => startsPerClientPerMinute;
        _time = time;
    }

    public bool TryAcquire(string clientKey)
    {
        var limit = _getLimit();
        if (limit <= 0)
        {
            return true;
        }

        var key = string.IsNullOrWhiteSpace(clientKey) ? "unknown" : clientKey.Trim();
        var now = _time.GetUtcNow();

        lock (_sync)
        {
            if (_buckets.Count > 1024)
            {
                SweepExpired(now);
            }

            if (!_buckets.TryGetValue(key, out var timestamps))
            {
                timestamps = new Queue<DateTimeOffset>();
                _buckets[key] = timestamps;
            }

            PruneQueue(timestamps, now);

            if (timestamps.Count >= limit)
            {
                return false;
            }

            timestamps.Enqueue(now);
            return true;
        }
    }

    private static void PruneQueue(Queue<DateTimeOffset> timestamps, DateTimeOffset now)
    {
        while (timestamps.Count > 0 && now - timestamps.Peek() >= Window)
        {
            timestamps.Dequeue();
        }
    }

    private void SweepExpired(DateTimeOffset now)
    {
        List<string>? emptyKeys = null;
        foreach (var (key, queue) in _buckets)
        {
            PruneQueue(queue, now);
            if (queue.Count == 0)
            {
                emptyKeys ??= [];
                emptyKeys.Add(key);
            }
        }

        if (emptyKeys is not null)
        {
            foreach (var key in emptyKeys)
            {
                _buckets.Remove(key);
            }
        }
    }
}
