using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.WikiDiff;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Web.Features.Investigations.Cache;

/// <summary>
/// Everything that shapes a run's result, hashed: pipeline/reader/wikidiff versions, the classify prompt's full version,
/// the Classify model, the run limits and the tier thresholds. A cached run is only replayed under the same fingerprint.
/// </summary>
public sealed class PipelineFingerprint(
    IOptions<InvestigationPipelineOptions> pipeline, IOptions<GeminiOptions> gemini, PromptLibrary prompts)
{
    private string? _value;

    public string Value => _value ??= Hash(Describe());

    public string Describe()
    {
        var o = pipeline.Value;
        var g = gemini.Value;
        var model = g.ModelFor(AnalysisJob.Classify);
        return $"pipeline={InvestigationPipeline.Version};reader={CorpusEditReader.Version};wikidiff={WikitextDiff.Contract};" +
               $"prompt={prompts.Get(ClassifyEditPrompt.Id).FullVersion};model={model};" +
               $"limits={o.ArticleLimit},{o.RevertsPerArticle},{o.MaxCandidates},{o.MaxSpanRevisions},{o.MaxPromptChars};" +
               $"signals={(o.EditWarSignal ? "edit_war" : "")},{(o.SourceChangeSignal ? "source_change" : "")};" +
               $"tiers={TierThresholds.Default}";
    }

    private static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..32];
}

/// <summary>A finished run as stored in <c>InvestigationRuns</c>, enough to replay its events.</summary>
public sealed record RunSnapshot(
    IReadOnlyList<TopicArticle> Articles,
    int RevisionsScanned,
    IReadOnlyList<RemovedEdit> Candidates,
    IReadOnlyList<ScoredSnapshot> Scored,
    IReadOnlyList<IncidentRecord> Incidents,
    DateTimeOffset AnalysedAt,
    double ElapsedMs,
    IReadOnlyDictionary<InvestigationStage, double> StageMs,
    double? FirstIncidentMs);

/// <param name="CandidateKey"><see cref="RemovedEdit.Key"/>.</param>
/// <param name="IncidentId"><see cref="IncidentRecord.Id"/> of the incident it became, if any.</param>
public sealed record ScoredSnapshot(string CandidateKey, string? IncidentId, string? Exclusion);

/// <summary>SQLite store of finished runs, keyed by fingerprint + normalised topic. Never throws for storage problems.</summary>
public sealed class InvestigationRunCache(
    IDbContextFactory<AppDbContext> dbFactory, PipelineFingerprint fingerprint, TimeProvider clock, ILogger<InvestigationRunCache> log)
{
    public const int MaxTopics = 50;

    public async Task<IReadOnlyList<string>> TopicsAsync(int limit = MaxTopics, CancellationToken ct = default)
    {
        if (limit <= 0)
        {
            return [];
        }

        var cap = Math.Min(limit, MaxTopics);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.Set<InvestigationRunRow>().AsNoTracking()
                .Where(r => r.Fingerprint == fingerprint.Value)
                .OrderByDescending(r => r.CreatedAt)
                .ThenBy(r => r.Topic)
                .Take(cap)
                .Select(r => r.Topic)
                .ToListAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            log.LogWarning(ex, "Could not list cached investigation topics");
            return [];
        }
    }

    public async Task<RunSnapshot?> TryGetAsync(string topic, CancellationToken ct = default)
    {
        var key = CachedQueryEmbedder.Normalize(topic);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var json = await db.Set<InvestigationRunRow>().AsNoTracking()
                .Where(r => r.Fingerprint == fingerprint.Value && r.Topic == key)
                .Select(r => r.Json).FirstOrDefaultAsync(ct);
            return json is null ? null : JsonSerializer.Deserialize<RunSnapshot>(json, IncidentJson.Options);
        }
        catch (Exception ex) when (ex is JsonException or DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            log.LogWarning(ex, "Investigation run cache for {Topic} unusable; running again", key);
            return null;
        }
    }

    public async Task StoreAsync(string topic, RunSnapshot snapshot, CancellationToken ct = default)
    {
        var key = CachedQueryEmbedder.Normalize(topic);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            await db.Set<InvestigationRunRow>().Where(r => r.Fingerprint == fingerprint.Value && r.Topic == key).ExecuteDeleteAsync(ct);
            db.Add(new InvestigationRunRow
            {
                Fingerprint = fingerprint.Value,
                Topic = key,
                Json = JsonSerializer.Serialize(snapshot, IncidentJson.Options),
                IncidentCount = snapshot.Incidents.Count,
                ElapsedMs = snapshot.ElapsedMs,
                CreatedAt = clock.GetUtcNow(),
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            log.LogWarning(ex, "Could not cache the investigation of {Topic}", key);
        }
    }

    /// <summary>Drops every cached run (all fingerprints), e.g. before measuring cold timings. Returns the rows deleted.</summary>
    public async Task<int> ClearAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Set<InvestigationRunRow>().ExecuteDeleteAsync(ct);
    }
}

/// <summary>
/// The app's <see cref="IInvestigationRunner"/> (#40): a topic analysed before under the same <see cref="PipelineFingerprint"/>
/// is replayed from SQLite at once (<c>Completed.FromCache</c>); otherwise <see cref="InvestigationPipeline"/> runs and a
/// clean result (no degraded candidates, no failure, not cancelled) is stored. <c>refresh</c> skips the lookup.
/// </summary>
public sealed class CachingInvestigationRunner(InvestigationPipeline inner, InvestigationRunCache cache, TimeProvider time)
    : IInvestigationRunner
{
    public async IAsyncEnumerable<InvestigationEvent> RunAsync(
        string topic, bool refresh = false, [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        topic = topic.Trim();

        if (!refresh && await cache.TryGetAsync(topic, ct) is { } snapshot)
        {
            foreach (var e in Replay(snapshot, time.GetTimestamp()))
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
            }
            yield break;
        }

        TopicArticle[] articles = [];
        RevisionsScanned? scan = null;
        var scored = new List<CandidateScored>();
        await foreach (var e in inner.RunAsync(topic, ct: ct))
        {
            switch (e)
            {
                case ArticlesFound a: articles = [.. a.Items]; break;
                case RevisionsScanned s: scan = s; break;
                case CandidateScored c: scored.Add(c); break;
                case Completed { Degraded: 0 } done when scan is not null:
                    await cache.StoreAsync(topic, new RunSnapshot(
                        articles, scan.Count, scan.Candidates,
                        [.. scored.Select(c => new ScoredSnapshot(c.Candidate.Key, c.Incident?.Id, c.Exclusion))],
                        done.Incidents, done.AnalysedAt, done.Elapsed.TotalMilliseconds,
                        done.StageTimings.ToDictionary(kv => kv.Key, kv => kv.Value.TotalMilliseconds),
                        done.FirstIncidentAfter?.TotalMilliseconds), ct);
                    break;
            }
            yield return e;
        }
    }

    private IEnumerable<InvestigationEvent> Replay(RunSnapshot s, long started)
    {
        var candidates = s.Candidates.ToDictionary(c => c.Key);
        var incidents = s.Incidents.Where(i => i.Id is not null).ToDictionary(i => i.Id!);

        yield return new ArticlesFound(s.Articles.Count, s.Articles);
        yield return new RevisionsScanned(s.RevisionsScanned, s.Candidates);
        var i = 0;
        foreach (var scored in s.Scored)
        {
            if (!candidates.TryGetValue(scored.CandidateKey, out var candidate)) continue;
            yield return new CandidateScored(++i, s.Scored.Count, candidate,
                scored.IncidentId is { } id ? incidents.GetValueOrDefault(id) : null, scored.Exclusion);
        }
        yield return new Completed(FromCache: true, s.Incidents, time.GetElapsedTime(started))
        {
            AnalysedAt = s.AnalysedAt,
            StageTimings = s.StageMs.ToDictionary(kv => kv.Key, kv => TimeSpan.FromMilliseconds(kv.Value)),
            FirstIncidentAfter = s.FirstIncidentMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
        };
    }
}
