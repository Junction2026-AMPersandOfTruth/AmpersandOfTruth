using System.Text.Json;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Web.Features.WebSpread.Store;

/// <summary>Per-incident result store (P3.7). Singleton; a DbContext per operation.</summary>
public sealed class WebSpreadResults(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time, ILogger<WebSpreadResults> log)
{
    /// <summary>Worth keeping: it ran, and nothing that weakens it silently (assessment down) happened.
    /// Partial results (a page that couldn't be read) are kept: the note says so, and a retry would usually fail the same way.</summary>
    public static bool IsStorable(WebSpreadResult r) =>
        r.Status is WebSpreadStatus.Completed or WebSpreadStatus.Partial && !r.AssessmentDegraded && r.SearchesRun > 0;

    public async Task<WebSpreadResult?> GetAsync(string incidentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Set<WebSpreadResultEntry>().AsNoTracking()
            .FirstOrDefaultAsync(e => e.IncidentId == incidentId && e.Version == WebSpreadTracer.Version, ct);
        if (row is null) return null;
        try
        {
            return JsonSerializer.Deserialize<WebSpreadResult>(row.ResultJson, WebSpreadJson.Options);
        }
        catch (JsonException ex)
        {
            log.LogWarning(ex, "Stored web-spread result for {Incident} is unreadable; ignoring it", incidentId);
            return null;
        }
    }

    /// <summary>Upserts when <see cref="IsStorable"/>; returns whether it was stored.</summary>
    public async Task<bool> SaveAsync(WebSpreadResult result, CancellationToken ct = default)
    {
        if (!IsStorable(result)) return false;
        var entry = new WebSpreadResultEntry
        {
            IncidentId = result.Claim.IncidentId,
            Version = WebSpreadTracer.Version,
            Status = result.Status,
            ResultJson = JsonSerializer.Serialize(result, WebSpreadJson.Options),
            CreatedAt = time.GetUtcNow(),
        };
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Set<WebSpreadResultEntry>().Where(e => e.IncidentId == entry.IncidentId).ExecuteDeleteAsync(ct);
        db.Set<WebSpreadResultEntry>().Add(entry);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<WebSpreadResultEntry>().ExecuteDeleteAsync(ct);
    }
}
