using System.Globalization;
using System.Text.Json;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Web.Features.Library;

public sealed class LibraryStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time)
{
    public const string DemoOwner = "demo";

    private const int MaxTitleLength = 200;
    private const int MaxTopicLength = 500;
    private const int MaxOwnerKeyLength = 64;
    private const int MaxIncidentIdLength = 100;
    private const int MaxNoteLength = 2000;

    public Task<IReadOnlyList<SavedSummary>> ListExamplesAsync(CancellationToken ct = default) =>
        ListAsync(DemoOwner, ct);

    public async Task<bool> IsDemoAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<SavedInvestigation>()
            .AsNoTracking()
            .AnyAsync(x => x.Id == id && x.OwnerKey == DemoOwner, ct);
    }

    private static bool IsReadOnlyOwner(string normalizedOwner) =>
        string.Equals(normalizedOwner, DemoOwner, StringComparison.OrdinalIgnoreCase);

    public async Task<Guid> SaveAsync(string ownerKey, string title, SavedSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentNullException.ThrowIfNull(snapshot);

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        var normalizedTopic = Clamp((snapshot.Topic ?? "").Trim(), MaxTopicLength);
        var slugSource = normalizedTopic.Length > 0 ? normalizedTopic : "investigation";
        var normalizedSlug = Clamp(TopicSlug.From(slugSource), MaxTopicLength);
        if (normalizedSlug.Length == 0)
        {
            normalizedSlug = "investigation";
        }

        var effectiveTitle = string.IsNullOrWhiteSpace(title) ? slugSource : title.Trim();
        effectiveTitle = Clamp(effectiveTitle, MaxTitleLength);

        var now = time.GetUtcNow();
        var id = Guid.NewGuid();
        var tierCounts = CountTiers(snapshot.Incidents);

        var entity = new SavedInvestigation
        {
            Id = id,
            Title = effectiveTitle,
            Topic = normalizedTopic,
            TopicSlug = normalizedSlug,
            CreatedAt = now,
            UpdatedAt = now,
            OwnerKey = normalizedOwner,
            PipelineVersion = InvestigationPipeline.Version.ToString(CultureInfo.InvariantCulture),
            SnapshotJson = JsonSerializer.Serialize(snapshot, IncidentJson.Options),
            IncidentCount = snapshot.Incidents.Count,
            TierCountsJson = SerializeTierCounts(tierCounts),
        };

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        db.Set<SavedInvestigation>().Add(entity);
        await db.SaveChangesAsync(ct);

        return id;
    }

    public async Task UpdateSnapshotAsync(Guid id, string ownerKey, SavedSnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        if (IsReadOnlyOwner(normalizedOwner))
        {
            return;
        }

        var now = time.GetUtcNow();
        var snapshotJson = JsonSerializer.Serialize(snapshot, IncidentJson.Options);
        var incidentCount = snapshot.Incidents.Count;
        var tierCountsJson = SerializeTierCounts(CountTiers(snapshot.Incidents));
        var pipelineVersion = InvestigationPipeline.Version.ToString(CultureInfo.InvariantCulture);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Set<SavedInvestigation>()
            .Where(x => x.Id == id && x.OwnerKey == normalizedOwner)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.SnapshotJson, snapshotJson)
                .SetProperty(x => x.IncidentCount, incidentCount)
                .SetProperty(x => x.TierCountsJson, tierCountsJson)
                .SetProperty(x => x.PipelineVersion, pipelineVersion)
                .SetProperty(x => x.UpdatedAt, now),
                ct);
    }

    public async Task<IReadOnlyList<SavedSummary>> ListAsync(string ownerKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return [];
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Set<SavedInvestigation>()
            .AsNoTracking()
            .Where(x => x.OwnerKey == normalizedOwner)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Topic,
                x.TopicSlug,
                x.CreatedAt,
                x.UpdatedAt,
                x.IncidentCount,
                x.TierCountsJson,
            })
            .ToListAsync(ct);

        var result = new List<SavedSummary>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(new SavedSummary(
                row.Id,
                row.Title,
                row.Topic,
                row.TopicSlug,
                row.CreatedAt,
                row.UpdatedAt,
                row.IncidentCount,
                DeserializeTierCounts(row.TierCountsJson)));
        }

        return result;
    }

    public async Task<(SavedSummary Summary, SavedSnapshot Snapshot)?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Set<SavedInvestigation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (row is null)
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<SavedSnapshot>(row.SnapshotJson, IncidentJson.Options);
        if (snapshot is null)
        {
            return null;
        }

        var summary = new SavedSummary(
            row.Id,
            row.Title,
            row.Topic,
            row.TopicSlug,
            row.CreatedAt,
            row.UpdatedAt,
            row.IncidentCount,
            DeserializeTierCounts(row.TierCountsJson));

        return (summary, snapshot);
    }

    public async Task<bool> RenameAsync(Guid id, string ownerKey, string title, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        if (IsReadOnlyOwner(normalizedOwner))
        {
            return false;
        }

        var normalizedTitle = Clamp(title.Trim(), MaxTitleLength);
        var now = time.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var updated = await db.Set<SavedInvestigation>()
            .Where(x => x.Id == id && x.OwnerKey == normalizedOwner)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Title, normalizedTitle)
                .SetProperty(x => x.UpdatedAt, now),
                ct);

        return updated > 0;
    }

    public async Task<bool> DeleteAsync(Guid id, string ownerKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        if (IsReadOnlyOwner(normalizedOwner))
        {
            return false;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var deleted = await db.Set<SavedInvestigation>()
            .Where(x => x.Id == id && x.OwnerKey == normalizedOwner)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    public async Task<IReadOnlyList<PinnedIncident>> PinsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<PinnedIncident>()
            .AsNoTracking()
            .Where(p => p.InvestigationId == id)
            .OrderBy(p => p.PinnedAt)
            .ThenBy(p => p.IncidentId)
            .ToListAsync(ct);
    }

    public async Task PinAsync(Guid id, string ownerKey, string incidentId, string? note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(incidentId))
        {
            return;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        if (IsReadOnlyOwner(normalizedOwner))
        {
            return;
        }

        var normalizedIncidentId = Clamp(incidentId.Trim(), MaxIncidentIdLength);
        var normalizedNote = note is null ? null : Clamp(note.Trim(), MaxNoteLength);
        var now = time.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ownsInvestigation = await db.Set<SavedInvestigation>()
            .AnyAsync(x => x.Id == id && x.OwnerKey == normalizedOwner, ct);

        if (!ownsInvestigation)
        {
            return;
        }

        var existing = await db.Set<PinnedIncident>()
            .FirstOrDefaultAsync(p => p.InvestigationId == id && p.IncidentId == normalizedIncidentId, ct);

        if (existing is not null)
        {
            existing.Note = normalizedNote;
        }
        else
        {
            db.Set<PinnedIncident>().Add(new PinnedIncident
            {
                InvestigationId = id,
                IncidentId = normalizedIncidentId,
                Note = normalizedNote,
                PinnedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UnpinAsync(Guid id, string ownerKey, string incidentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(incidentId))
        {
            return;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        if (IsReadOnlyOwner(normalizedOwner))
        {
            return;
        }

        var normalizedIncidentId = Clamp(incidentId.Trim(), MaxIncidentIdLength);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ownsInvestigation = await db.Set<SavedInvestigation>()
            .AnyAsync(x => x.Id == id && x.OwnerKey == normalizedOwner, ct);

        if (!ownsInvestigation)
        {
            return;
        }

        await db.Set<PinnedIncident>()
            .Where(p => p.InvestigationId == id && p.IncidentId == normalizedIncidentId)
            .ExecuteDeleteAsync(ct);
    }

    private static Dictionary<EvidenceTier, int> CountTiers(IReadOnlyList<IncidentRecord> incidents)
    {
        var counts = new Dictionary<EvidenceTier, int>
        {
            [EvidenceTier.Reverted] = 0,
            [EvidenceTier.Contested] = 0,
            [EvidenceTier.Campaign] = 0,
        };

        foreach (var incident in incidents)
        {
            if (counts.TryGetValue(incident.Tier, out var current))
            {
                counts[incident.Tier] = current + 1;
            }
        }

        return counts;
    }

    private static string SerializeTierCounts(IReadOnlyDictionary<EvidenceTier, int> counts)
    {
        var map = new Dictionary<string, int>(3, StringComparer.Ordinal);
        foreach (var info in EvidenceTiers.All)
        {
            map[info.Id] = counts.GetValueOrDefault(info.Tier, 0);
        }

        return JsonSerializer.Serialize(map);
    }

    private static IReadOnlyDictionary<EvidenceTier, int> DeserializeTierCounts(string? json)
    {
        var counts = new Dictionary<EvidenceTier, int>
        {
            [EvidenceTier.Reverted] = 0,
            [EvidenceTier.Contested] = 0,
            [EvidenceTier.Campaign] = 0,
        };

        if (string.IsNullOrWhiteSpace(json))
        {
            return counts;
        }

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (raw is null)
            {
                return counts;
            }

            foreach (var (key, value) in raw)
            {
                if (EvidenceTiers.TryParseId(key.ToLowerInvariant(), out var tier))
                {
                    counts[tier] = value;
                }
                else if (Enum.TryParse<EvidenceTier>(key, ignoreCase: true, out var enumTier) && Enum.IsDefined(enumTier))
                {
                    counts[enumTier] = value;
                }
            }
        }
        catch (JsonException)
        {
            // Keep zeroed defaults on corrupt tier JSON.
        }

        return counts;
    }

    private static string Clamp(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
