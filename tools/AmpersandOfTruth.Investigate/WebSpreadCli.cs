using System.Text.Json;
using System.Text.Json.Serialization;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using AmpersandOfTruth.Web.Features.WebSpread.Store;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Investigate;

/// <summary>P3 web spread from the shell (<c>scripts/webspread.sh</c>): the app's own tracer, Oxylabs client, caches and caps.</summary>
public static partial class InvestigateCli
{
    private static readonly JsonSerializerOptions SpreadJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // readable Finnish on a terminal
    };

    /// <summary>Runs (or replays) the topic's investigation, maps its Nth incident like the UI does, and traces it.</summary>
    private static async Task<int> WebSpreadTopicAsync(IServiceProvider services, string topic, int rank, bool events, CancellationToken ct)
    {
        var runner = services.GetRequiredService<IInvestigationRunner>();
        IReadOnlyList<IncidentRecord>? incidents = null;
        await foreach (var e in runner.RunAsync(topic, false, ct))
        {
            if (e is Completed c) incidents = c.Incidents;
            if (e is StageFailed f)
            {
                Console.Error.WriteLine($"investigation failed at {f.Stage} ({f.Kind}): {f.Error}");
                return 1;
            }
        }
        var ranked = (incidents ?? []).OrderByDescending(i => i.Score).ToList();
        if (rank < 1 || rank > ranked.Count)
        {
            Console.Error.WriteLine($"'{topic}' has {ranked.Count} incident(s); --rank {rank} is out of range");
            return 1;
        }

        var view = await services.GetRequiredService<IncidentMapper>().MapAsync(ranked[rank - 1], rank, ct);
        var claim = WebSpreadClaim.From(view);
        Console.Error.WriteLine($"tracing #{rank} {claim.IncidentId} on {claim.ArticleTitle}: {claim.ClaimEn ?? claim.ClaimFi ?? claim.InsertedText}");
        return await TraceAsync(services, claim, events, ct);
    }

    /// <summary>
    /// A hand-written claim on stdin, e.g.
    /// <code>{"incident_id":"mainila-test","article_title":"Mainilan laukaukset","claim_en":"Finland fired the Mainila shots.",
    ///  "inserted_text":"…","windows":[{"from":"2014-03-01T12:00:00Z","until":"2014-03-20T09:00:00Z"}]}</code>
    /// </summary>
    private static async Task<int> WebSpreadClaimAsync(IServiceProvider services, string json, bool events, CancellationToken ct)
    {
        WebSpreadClaim? claim;
        try { claim = JsonSerializer.Deserialize<WebSpreadClaim>(json, SpreadJson); }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"bad claim JSON: {ex.Message}");
            return 2;
        }
        if (claim is null || string.IsNullOrWhiteSpace(claim.IncidentId))
        {
            Console.Error.WriteLine("claim JSON needs at least incident_id, article_title and one of inserted_text / claim_fi / claim_en");
            return 2;
        }
        return await TraceAsync(services, claim with { Windows = claim.Windows ?? [] }, events, ct);
    }

    private static async Task<int> TraceAsync(IServiceProvider services, WebSpreadClaim claim, bool events, CancellationToken ct)
    {
        var tracer = services.GetRequiredService<WebSpreadTracer>();
        WebSpreadResult? result = null;
        await foreach (var e in tracer.TraceAsync(claim, ct))
        {
            if (events) Console.Out.WriteLine(JsonSerializer.Serialize<object>(new { @event = e.GetType().Name, data = e }, SpreadJson));
            switch (e)
            {
                case WebSpreadPlanned p:
                    Console.Error.WriteLine($"plan: {p.Plan.Queries.Count} searches{(p.Plan.Degraded is { } d ? $" ({d})" : "")}");
                    break;
                case WebSpreadSearched s:
                    Console.Error.WriteLine($"  search [{s.Query.Language}/{s.Query.Kind}/{s.Query.WindowKind}] {s.Outcome} {s.Results} results{(s.FromCache ? " (cached)" : "")}{(s.Error is { } err ? " — " + err : "")}  {s.Query.Text}");
                    break;
                case WebSpreadReading r:
                    Console.Error.WriteLine($"reading {r.Pages} pages + {r.Mirrors} mirrors");
                    break;
                case WebSpreadSourceFound f:
                    Console.Error.WriteLine($"  source [{f.Source.Bucket}/{f.Source.Stance}/{f.Source.MatchType}] {f.Source.Dating.EffectiveDate?.ToString("yyyy-MM-dd") ?? "undated"} ({f.Source.Dating.Method}, {f.Source.Dating.Confidence}) {f.Source.Url}");
                    break;
                case WebSpreadCompleted c:
                    result = c.Result;
                    break;
            }
        }

        var usage = await services.GetRequiredService<WebSpreadStore>().UsageAsync(claim.Scope, ct);
        Console.Error.WriteLine($"{result?.Status}: {result?.Headline}");
        Console.Error.WriteLine($"live web requests: {usage.LiveInScope} this trace, {usage.LiveToday} today; cache hits today {usage.CacheHitsToday}");
        if (!events && result is not null) Console.Out.WriteLine(JsonSerializer.Serialize(result, SpreadJson));
        return result?.Status is WebSpreadStatus.Completed or WebSpreadStatus.Partial ? 0 : 1;
    }
}
