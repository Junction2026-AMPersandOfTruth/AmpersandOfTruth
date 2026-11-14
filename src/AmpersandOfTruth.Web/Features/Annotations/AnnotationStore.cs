using System.Text.Json;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Library;
using Microsoft.EntityFrameworkCore;

namespace AmpersandOfTruth.Web.Features.Annotations;

public sealed class AnnotationStore(IDbContextFactory<AppDbContext> dbFactory, TimeProvider time)
{
    private const int MaxOwnerKeyLength = 64;
    private const int MaxIncidentIdLength = 100;
    private const int MaxNoteLength = 4000;
    private const int MaxSourceLength = 32;

    public async Task<IReadOnlyDictionary<string, IncidentAnnotation>> ForInvestigationAsync(
        Guid id,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Set<IncidentAnnotation>()
            .AsNoTracking()
            .Where(x => x.InvestigationId == id)
            .ToListAsync(ct);

        var result = new Dictionary<string, IncidentAnnotation>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            result[row.IncidentId] = row;
        }

        return result;
    }

    public async Task<bool> UpsertAsync(
        Guid id,
        string ownerKey,
        string incidentId,
        AnalystVerdict? verdict,
        DismissReason? dismiss,
        string? note,
        string? source,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(incidentId))
        {
            return false;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        var normalizedIncidentId = Clamp(incidentId.Trim(), MaxIncidentIdLength);
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : Clamp(note.Trim(), MaxNoteLength);
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? null : Clamp(source.Trim(), MaxSourceLength);
        var now = time.GetUtcNow();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ownsInvestigation = await db.Set<SavedInvestigation>()
            .AnyAsync(x => x.Id == id && x.OwnerKey == normalizedOwner, ct);
        if (!ownsInvestigation)
        {
            return false;
        }

        var existing = await db.Set<IncidentAnnotation>()
            .FirstOrDefaultAsync(x => x.InvestigationId == id && x.IncidentId == normalizedIncidentId, ct);

        if (existing is null)
        {
            db.Set<IncidentAnnotation>().Add(new IncidentAnnotation
            {
                InvestigationId = id,
                IncidentId = normalizedIncidentId,
                Verdict = verdict,
                DismissReason = dismiss,
                Note = normalizedNote,
                Source = normalizedSource,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.Verdict = verdict;
            existing.DismissReason = dismiss;
            existing.Note = normalizedNote;
            existing.Source = normalizedSource;
            existing.UpdatedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ClearAsync(
        Guid id,
        string ownerKey,
        string incidentId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(incidentId))
        {
            return false;
        }

        var normalizedOwner = Clamp(ownerKey.Trim(), MaxOwnerKeyLength);
        var normalizedIncidentId = Clamp(incidentId.Trim(), MaxIncidentIdLength);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ownsInvestigation = await db.Set<SavedInvestigation>()
            .AnyAsync(x => x.Id == id && x.OwnerKey == normalizedOwner, ct);
        if (!ownsInvestigation)
        {
            return false;
        }

        var deleted = await db.Set<IncidentAnnotation>()
            .Where(x => x.InvestigationId == id && x.IncidentId == normalizedIncidentId)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    public async Task<IReadOnlyList<AnnotatedIncident>> AllAnnotatedIncidentsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var annotations = await db.Set<IncidentAnnotation>()
            .AsNoTracking()
            .ToListAsync(ct);

        if (annotations.Count == 0)
        {
            return [];
        }

        annotations.Sort(static (a, b) =>
        {
            var cmp = b.UpdatedAt.CompareTo(a.UpdatedAt);
            return cmp != 0 ? cmp : StringComparer.Ordinal.Compare(a.IncidentId, b.IncidentId);
        });

        var invIds = annotations.Select(a => a.InvestigationId).Distinct().ToList();
        var savedRows = await db.Set<SavedInvestigation>()
            .AsNoTracking()
            .Where(x => invIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Topic, x.TopicSlug, x.SnapshotJson })
            .ToListAsync(ct);

        var incidentsByInv = new Dictionary<Guid, (string Topic, string TopicSlug, Dictionary<string, IncidentRecord> ById)>(savedRows.Count);
        foreach (var row in savedRows)
        {
            SavedSnapshot? snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<SavedSnapshot>(row.SnapshotJson, IncidentJson.Options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (snapshot is null)
            {
                continue;
            }

            var byId = new Dictionary<string, IncidentRecord>(snapshot.Incidents.Count, StringComparer.Ordinal);
            foreach (var inc in snapshot.Incidents)
            {
                byId[AnnotationExport.ResolveIncidentId(inc)] = inc;
            }

            incidentsByInv[row.Id] = (row.Topic, row.TopicSlug, byId);
        }

        var result = new List<AnnotatedIncident>(annotations.Count);
        foreach (var ann in annotations)
        {
            if (incidentsByInv.TryGetValue(ann.InvestigationId, out var inv)
                && inv.ById.TryGetValue(ann.IncidentId, out var record))
            {
                result.Add(new AnnotatedIncident(
                    ann.InvestigationId,
                    inv.Topic,
                    inv.TopicSlug,
                    ann.IncidentId,
                    record,
                    ann));
            }
        }

        return result;
    }

    private static string Clamp(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
