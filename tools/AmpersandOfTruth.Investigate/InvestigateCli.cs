using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AmpersandOfTruth.Web.Data;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Corpus;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace AmpersandOfTruth.Investigate;

/// <summary>
/// The app's investigation pipeline (#39) from the shell, with the app's own services, config and caches (#40).
/// stdout = results only; logs = stderr. See <c>scripts/investigate.sh</c>.
/// </summary>
public static class InvestigateCli
{
    private const string Usage = """
        usage:
          investigate run <topic> [--refresh] [--events]   one topic → JSON summary (or every event as JSONL with --events)
          investigate warm <topics-file> [--cold]          each topic twice (fill cache, then warm) → Markdown timing table
          investigate variant                              eval contract §2 request on stdin → response on stdout
          investigate clear-runs                           drop every cached run (readings and LLM replies stay cached)
        options: --db <app.db path>
        env (eval contract §2): CORPUS_HOST CORPUS_PORT CORPUS_USER CORPUS_PW CORPUS_DB OLLAMA_URL — each overrides app config.
        """;

    private static readonly JsonSerializerOptions Out = new(IncidentJson.Options) { WriteIndented = false };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var rest = args.ToList();
        var db = TakeOption(rest, "--db");
        if (rest.Count == 0 || rest[0] is "-h" or "--help")
        {
            Console.Error.WriteLine(Usage);
            return rest.Count == 0 ? 2 : 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        try
        {
            switch (rest[0])
            {
                case "run" when rest.Count >= 2:
                {
                    var refresh = rest.Remove("--refresh");
                    var events = rest.Remove("--events");
                    using var host = await BuildHostAsync(db, [], cts.Token);
                    return await RunAsync(host.Services, string.Join(' ', rest.Skip(1)), refresh, events, cts.Token);
                }
                case "warm" when rest.Count == 2 || rest.Count == 3 && rest.Contains("--cold"):
                {
                    var cold = rest.Remove("--cold");
                    using var host = await BuildHostAsync(db, [], cts.Token);
                    return await WarmAsync(host.Services, rest[1], cold, cts.Token);
                }
                case "variant":
                    return await VariantAsync(db, cts.Token);
                case "clear-runs":
                {
                    using var host = await BuildHostAsync(db, [], cts.Token);
                    var n = await host.Services.GetRequiredService<InvestigationRunCache>().ClearAsync(cts.Token);
                    Console.Error.WriteLine($"cleared {n} cached run(s)");
                    return 0;
                }
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Console.Error.WriteLine("cancelled");
            return 130;
        }
    }

    // ── run ──────────────────────────────────────────────────────────────────

    private static async Task<int> RunAsync(IServiceProvider services, string topic, bool refresh, bool events, CancellationToken ct)
    {
        var runner = services.GetRequiredService<IInvestigationRunner>();
        Completed? done = null;
        StageFailed? failed = null;
        var articles = 0;
        var candidates = 0;
        await foreach (var e in runner.RunAsync(topic, refresh, ct))
        {
            if (events) Console.Out.WriteLine(EventJson(e));
            switch (e)
            {
                case ArticlesFound a: articles = a.Count; break;
                case RevisionsScanned s: candidates = s.Candidates.Count; break;
                case Completed c: done = c; break;
                case StageFailed f: failed = f; break;
            }
        }

        if (failed is not null)
        {
            Console.Error.WriteLine($"{failed.Stage} failed ({failed.Kind}): {failed.Error}");
            return 1;
        }
        if (!events && done is not null)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                Topic = topic,
                done.FromCache,
                Articles = articles,
                Candidates = candidates,
                done.Degraded,
                Timings = Timings(done),
                done.Incidents,
            }, Out));
        }
        return 0;
    }

    // ── warm ─────────────────────────────────────────────────────────────────

    private sealed record Pass(bool FromCache, int Articles, int Candidates, int Incidents, int Degraded, double? FirstMs, double TotalMs, string? Error);

    private static async Task<int> WarmAsync(IServiceProvider services, string topicsFile, bool cold, CancellationToken ct)
    {
        var topics = File.ReadAllLines(topicsFile)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();
        if (cold)
            Console.Error.WriteLine($"cleared {await services.GetRequiredService<InvestigationRunCache>().ClearAsync(ct)} cached run(s)");

        var runner = services.GetRequiredService<IInvestigationRunner>();
        var rows = new List<(string Topic, Pass First, Pass Second)>();
        var failures = 0;
        foreach (var topic in topics)
        {
            Console.Error.WriteLine($"── {topic}");
            var first = await PassAsync(runner, topic, ct);
            var second = await PassAsync(runner, topic, ct);
            if (first.Error is not null) failures++;
            rows.Add((topic, first, second));
        }

        static string Ms(double? ms) => ms is { } v ? (v / 1000).ToString("0.0", CultureInfo.InvariantCulture) + " s" : "–";
        static string Kind(Pass p) => p.Error is not null ? "failed" : p.FromCache ? "warm" : "cold";

        var md = new StringBuilder();
        md.AppendLine("| Topic | Articles | Removed edits | Incidents | Degraded | Pass 1 | First incident | Total | Pass 2 | Total |");
        md.AppendLine("|---|---:|---:|---:|---:|---|---:|---:|---|---:|");
        foreach (var (topic, a, b) in rows)
            md.AppendLine($"| {topic} | {a.Articles} | {a.Candidates} | {a.Incidents} | {a.Degraded} | {Kind(a)} | {Ms(a.FirstMs)} | {Ms(a.TotalMs)} | {Kind(b)} | {Ms(b.TotalMs)} |");
        md.AppendLine();
        md.AppendLine("Budget (#40): first incident < 8 s and total < 30 s cold; total < 2 s warm. " +
                      "A pass is cold when the run cache missed (diff and LLM caches may still be warm; `--cold` only clears the run cache). " +
                      "Degraded > 0 = some assessment unavailable, so that run was not cached.");
        foreach (var (topic, a, b) in rows.Where(r => r.First.Error is not null || r.Second.Error is not null))
            md.AppendLine($"- {topic}: {a.Error ?? b.Error}");
        Console.Out.Write(md.ToString());
        return failures == 0 ? 0 : 1;
    }

    private static async Task<Pass> PassAsync(IInvestigationRunner runner, string topic, CancellationToken ct)
    {
        int articles = 0, candidates = 0;
        await foreach (var e in runner.RunAsync(topic, ct: ct))
        {
            switch (e)
            {
                case ArticlesFound a: articles = a.Count; break;
                case RevisionsScanned s: candidates = s.Candidates.Count; break;
                case StageFailed f: return new Pass(false, articles, candidates, 0, 0, null, 0, $"{f.Stage}: {f.Error}");
                case Completed c:
                    return new Pass(c.FromCache, articles, candidates, c.Incidents.Count, c.Degraded,
                        c.FirstIncidentAfter?.TotalMilliseconds, c.Elapsed.TotalMilliseconds, null);
            }
        }
        return new Pass(false, articles, candidates, 0, 0, null, 0, "no result");
    }

    // ── variant (eval contract §2) ───────────────────────────────────────────

    private static async Task<int> VariantAsync(string? db, CancellationToken ct)
    {
        var request = JsonNode.Parse(await Console.In.ReadToEndAsync(ct))?.AsObject()
            ?? throw new InvalidOperationException("empty request");
        var kind = request["kind"]?.GetValue<string>();
        var text = request["text"]?.GetValue<string>() ?? "";
        var k = request["k"]?.GetValue<int>();
        var limit = request["limit"]?.GetValue<int>() ?? 50;

        Dictionary<string, string?> overrides = [];
        if (k is { } articleLimit) overrides[$"{InvestigationPipelineOptions.Section}:ArticleLimit"] = articleLimit.ToString(CultureInfo.InvariantCulture);
        using var host = await BuildHostAsync(db, overrides, ct);
        var services = host.Services;
        var pipeline = services.GetRequiredService<InvestigationPipeline>();

        JsonObject response;
        try
        {
            switch (kind)
            {
                case "retrieval":
                {
                    var search = services.GetRequiredService<ITopicSearch>();
                    var found = await search.SearchAsync(text, ct);
                    Console.Error.WriteLine($"search {search.Fingerprint}: {found.Articles.Count} articles, scope {found.Scope}, fallback {found.Fallback}");
                    // R6 (#27): the scope verdict is reported (R3-E's oos metric) but results are still returned, as in the app.
                    response = new JsonObject
                    {
                        ["contract"] = 1, ["out_of_scope"] = OutOfScope(found.Scope), ["results"] = Results(found.Articles, llm: found.Scope is not null),
                    };
                    break;
                }
                case "signals":
                {
                    var ids = request["page_ids"]!.AsArray().Select(n => n!.GetValue<long>()).ToList();
                    var found = (await services.GetRequiredService<IInvestigationCorpus>().FiArticlesAsync(ids, ct)).ToDictionary(a => a.PageId);
                    var articles = ids.Where(found.ContainsKey).Select(id => found[id]).ToList();
                    var (_, incidents) = await CollectAsync(pipeline.RunOnArticlesAsync(text, articles, ct));
                    response = new JsonObject { ["contract"] = 1, ["incidents"] = Incidents(incidents, limit) };
                    break;
                }
                case "pipeline":
                {
                    var (found, incidents) = await CollectAsync(pipeline.RunAsync(text, ct: ct));
                    response = new JsonObject
                    {
                        ["contract"] = 1, ["out_of_scope"] = OutOfScope(found.Scope), ["results"] = Results(found.Items, llm: found.Scope is not null),
                        ["incidents"] = Incidents(incidents, limit),
                    };
                    break;
                }
                default:
                    Console.Error.WriteLine($"unknown request kind '{kind}'");
                    return 2;
            }
        }
        catch (Exception ex) when (ex is EmbeddingUnavailableException or CorpusUnavailableException or StageFailedException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        Console.Out.WriteLine(response.ToJsonString());
        return 0;
    }

    private sealed class StageFailedException(StageFailed f) : Exception($"{f.Stage} failed ({f.Kind}): {f.Error}");

    private static async Task<(ArticlesFound Articles, IReadOnlyList<IncidentRecord> Incidents)> CollectAsync(
        IAsyncEnumerable<InvestigationEvent> run)
    {
        var articles = new ArticlesFound(0, []);
        await foreach (var e in run)
        {
            switch (e)
            {
                case ArticlesFound a: articles = a; break;
                case StageFailed f: throw new StageFailedException(f);
                case Completed c:
                    Console.Error.WriteLine($"{c.Incidents.Count} incidents, {c.Degraded} degraded, {Timings(c).ToJsonString()}");
                    return (articles, c.Incidents);
            }
        }
        throw new InvalidOperationException("the pipeline ended without a result");
    }

    private static bool OutOfScope(TopicScope? scope) => scope is { InScope: false };

    /// <summary>Contract §2.1 results in rank order (the runner scores the order; <c>score</c> is informational). <c>why</c>:
    /// <c>keyword</c> = an LLM title guess matched, <c>vector</c> = bge-m3, <c>llm</c> = the expansion shaped the query.</summary>
    private static JsonArray Results(IReadOnlyList<TopicArticle> articles, bool llm) =>
        [.. articles.Where(a => a.HasHistory).Select((a, i) => (JsonNode)new JsonObject
        {
            ["page_id"] = a.PageId,
            ["score"] = Math.Round(a.TitleMatch ? 2 - i * 0.01 : 1 - a.Distance, 5),
            ["why"] = a.TitleMatch ? new JsonArray("keyword", "llm") : llm ? new JsonArray("vector", "llm") : new JsonArray("vector"),
        })];

    private static JsonArray Incidents(IReadOnlyList<IncidentRecord> incidents, int limit) =>
        [.. incidents.Take(limit).Select(i => JsonSerializer.SerializeToNode(i, Out))];

    // ── shared ───────────────────────────────────────────────────────────────

    private static JsonObject Timings(Completed c)
    {
        var o = new JsonObject();
        foreach (var (stage, t) in c.StageTimings) o[IncidentJson.Id(stage) + "_ms"] = Math.Round(t.TotalMilliseconds);
        o["first_incident_ms"] = c.FirstIncidentAfter is { } f ? Math.Round(f.TotalMilliseconds) : null;
        o["total_ms"] = Math.Round(c.Elapsed.TotalMilliseconds);
        return o;
    }

    private static string EventJson(InvestigationEvent e)
    {
        var node = JsonSerializer.SerializeToNode(e, e.GetType(), Out)!.AsObject();
        node.Insert(0, "event", IncidentJson.Id(Enum.Parse<EventKind>(e.GetType().Name)));
        return node.ToJsonString();
    }

    private enum EventKind { ArticlesFound, RevisionsScanned, CandidateScored, StageFailed, Completed }

    private static string? TakeOption(List<string> args, string name)
    {
        var i = args.IndexOf(name);
        if (i < 0 || i == args.Count - 1) return null;
        var value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }

    private static async Task<IHost> BuildHostAsync(string? dbOverride, Dictionary<string, string?> overrides, CancellationToken ct)
    {
        var root = FindRepoRoot();
        var web = Path.Combine(root, "src", "AmpersandOfTruth.Web");
        var dbPath = dbOverride is not null ? Path.GetFullPath(dbOverride) : Path.Combine(web, "app.db");

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile(Path.Combine(web, "appsettings.json"), optional: false, reloadOnChange: false)
            .AddUserSecrets(typeof(AnalysisFeature).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(ContractEnvironment(builder.Configuration))
            .AddInMemoryCollection(overrides)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:App"] = $"Data Source={dbPath}" });

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("System", LogLevel.Warning);
        builder.Logging.AddFilter("AmpersandOfTruth", LogLevel.Information);
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAppData();
        builder.Services.AddCorpusFeature();
        builder.Services.AddEmbeddingFeature();
        builder.Services.AddAnalysisFeature();
        builder.Services.AddInvestigationsFeature();

        var host = builder.Build();
        await using var db = await host.Services.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        return host;
    }

    /// <summary>The eval contract's env vars (§2), mapped onto the app's config keys. Unset vars change nothing.</summary>
    private static Dictionary<string, string?> ContractEnvironment(IConfiguration config)
    {
        var map = new Dictionary<string, string?>();
        string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

        if ((Env("CORPUS_HOST") ?? Env("CORPUS_PORT") ?? Env("CORPUS_USER") ?? Env("CORPUS_DB")) is not null)
        {
            var b = new MySqlConnectionStringBuilder(config.GetConnectionString("Corpus") ?? "");
            if (Env("CORPUS_HOST") is { } host) b.Server = host;
            if (Env("CORPUS_PORT") is { } port) b.Port = uint.Parse(port, CultureInfo.InvariantCulture);
            if (Env("CORPUS_USER") is { } user) b.UserID = user;
            if (Env("CORPUS_DB") is { } database) b.Database = database;
            map["ConnectionStrings:Corpus"] = b.ConnectionString;
        }
        if (Env("CORPUS_PW") is { } pw) map["Corpus:Password"] = pw;
        if (Env("OLLAMA_URL") is { } ollama) map["Embedding:OllamaUrl"] = ollama;
        return map;
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(Path.GetFullPath(start)); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln"))) return dir.FullName;
        }
        throw new InvalidOperationException("Could not find the repository root (AmpersandOfTruth.sln).");
    }
}
