using AmpersandOfTruth.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Web.Features.WebSpread.Store;

/// <param name="LiveToday">Live requests since UTC midnight, whole app.</param>
/// <param name="LiveInScope">Live requests in the asked scope (0 when no scope was asked).</param>
public sealed record WebUsage(int LiveToday, int LiveInScope, int CacheHitsToday);

/// <summary>SQLite cache + ledger for web spread (P3.3). Singleton; a DbContext per operation.</summary>
public sealed class WebSpreadStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time)
{
    /// <summary>Fires after every ledger write (the dev footer's live counter).</summary>
    public event Action? Changed;

    public async Task<WebCacheEntry?> GetAsync(string key, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<WebCacheEntry>().AsNoTracking().FirstOrDefaultAsync(e => e.Key == key, ct);
    }

    /// <summary>Upsert by key.</summary>
    public async Task PutAsync(WebCacheEntry entry, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Set<WebCacheEntry>().Where(e => e.Key == entry.Key).ExecuteDeleteAsync(ct);
        db.Set<WebCacheEntry>().Add(entry);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RecordAsync(WebCallRecord record, CancellationToken ct = default)
    {
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            db.Set<WebCallRecord>().Add(record);
            await db.SaveChangesAsync(ct);
        }
        Changed?.Invoke();
    }

    public async Task<WebUsage> UsageAsync(string? scope = null, CancellationToken ct = default)
    {
        var since = new DateTimeOffset(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var today = db.Set<WebCallRecord>().AsNoTracking().Where(r => r.At >= since);
        var liveToday = await today.CountAsync(r => r.Live, ct);
        var hits = await today.CountAsync(r => r.FromCache, ct);
        var inScope = scope is null ? 0 : await db.Set<WebCallRecord>().AsNoTracking().CountAsync(r => r.Live && r.Scope == scope, ct);
        return new WebUsage(liveToday, inScope, hits);
    }

    /// <summary>Deletes the cache (all, or one kind). Returns rows deleted.</summary>
    public async Task<int> ClearCacheAsync(string? kind = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<WebCacheEntry> q = db.Set<WebCacheEntry>();
        if (kind is not null) q = q.Where(e => e.Kind == kind);
        return await q.ExecuteDeleteAsync(ct);
    }
}
