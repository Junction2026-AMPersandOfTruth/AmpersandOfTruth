using System.Text.Json;
using System.Text.Json.Serialization;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.WikiDiff;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.Cache;

/// <summary>
/// Stage 3a with a SQLite cache in front (<c>EditReadingCache</c>, #40): a removed edit is fetched and diffed once, ever.
/// Key = page + restored + last undone revision + reader version + which signals are on. Failures are not cached, and
/// neither is a reading whose conflict episodes could not be read (it would freeze "unknown"). A broken cache row is
/// re-read, and a storage problem never costs the reading itself (the edit would count as unreadable and the run as
/// degraded). Singleton, thread-safe. <paramref name="inner"/> = the uncached reader (<see cref="CorpusEditReader"/>).
/// </summary>
public sealed class CachedEditReader(
    IEditReader inner, IOptions<InvestigationPipelineOptions> options, IDbContextFactory<AppDbContext> dbFactory,
    TimeProvider clock, ILogger<CachedEditReader> log) : IEditReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Payload(
        IReadOnlyList<RevisionRow> Span, WikitextDiffResult Diff, string InsertedText, string RemovedText, string Context,
        IReadOnlyList<RevisionRow> Reinsertions, IReadOnlyList<ConflictEpisode> Conflicts, IReadOnlyList<SourceChange> SourceChanges);

    public string KeyFor(RemovedEdit edit) =>
        $"{edit.Article.PageId}:{edit.Revert.RestoredRevId}:{edit.Revert.RevertedRevId}" +
        $":r{CorpusEditReader.Version}.d{WikitextDiff.Contract}.c{options.Value.MaxPromptChars}" +
        $".s{(options.Value.EditWarSignal ? 1 : 0)}{(options.Value.SourceChangeSignal ? 1 : 0)}";

    public async Task<EditReading> ReadAsync(RemovedEdit edit, CancellationToken ct = default)
    {
        var key = KeyFor(edit);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var json = await db.Set<EditReadingCacheRow>().AsNoTracking()
                .Where(r => r.Key == key).Select(r => r.Json).FirstOrDefaultAsync(ct);
            if (json is not null && JsonSerializer.Deserialize<Payload>(json, Json) is { Span.Count: > 0, Conflicts: not null, SourceChanges: not null } p)
            {
                return new EditReading(edit, p.Span, p.Diff, p.InsertedText, p.RemovedText, p.Context)
                {
                    Reinsertions = p.Reinsertions, Conflicts = p.Conflicts, SourceChanges = p.SourceChanges,
                };
            }
        }
        catch (Exception ex) when (ex is JsonException or DbUpdateException or InvalidOperationException or SqliteException)
        {
            log.LogWarning(ex, "Edit reading cache row {Key} unusable; reading again", key);
        }

        var reading = await inner.ReadAsync(edit, ct);
        if (reading.Conflicts is not { } conflicts) return reading;   // unknown today, readable tomorrow: read again then
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Set<EditReadingCacheRow>().Where(r => r.Key == key).ExecuteDeleteAsync(ct);
            db.Add(new EditReadingCacheRow
            {
                Key = key,
                PageId = edit.Article.PageId,
                RevertRevId = edit.Revert.RevId,
                Json = JsonSerializer.Serialize(
                    new Payload(reading.Span, reading.Diff, reading.InsertedText, reading.RemovedText, reading.Context,
                        reading.Reinsertions, conflicts, reading.SourceChanges), Json),
                CreatedAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            log.LogDebug(ex, "Edit reading {Key} already cached by a concurrent run", key); // identical content
        }
        catch (Exception ex) when (ex is InvalidOperationException or SqliteException)
        {
            log.LogWarning(ex, "Could not cache edit reading {Key}", key);
        }
        return reading;
    }
}
