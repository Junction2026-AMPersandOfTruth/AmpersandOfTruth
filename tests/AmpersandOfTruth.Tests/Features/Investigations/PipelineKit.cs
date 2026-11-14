using AmpersandOfTruth.Tests.Features.Embedding;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Embedding;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Analysis.Prompts;
using AmpersandOfTruth.Web.Features.Investigations.Cache;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Features.Investigations;

/// <summary>In-memory <c>corpus.revision</c> + <c>revision_text</c>. <see cref="Unavailable"/> simulates the tunnel being down.</summary>
public sealed class FakeRevisionStore : IRevisionStore
{
    public List<RevisionRow> Rows { get; } = [];
    public Dictionary<(long Page, long Rev), string> Texts { get; } = [];
    public bool Unavailable { get; set; }
    public HashSet<long> BrokenRevisions { get; } = [];
    private int _textReads;
    public int TextReads => _textReads;

    public Task<int> RevisionCountAsync(IReadOnlyCollection<long> pageIds, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult(Rows.Count(r => pageIds.Contains(r.PageId)));
    }

    public Task<IReadOnlyList<RevisionRow>> SpanAsync(long pageId, long restoredRevId, long lastRevId, int limit, CancellationToken ct = default)
    {
        ThrowIfDown();
        return Task.FromResult<IReadOnlyList<RevisionRow>>(Rows
            .Where(r => r.PageId == pageId && r.RevId > restoredRevId && r.RevId <= lastRevId)
            .OrderBy(r => r.RevId).Take(limit).ToList());
    }

    public Task<string?> TextAsync(long pageId, long revId, CancellationToken ct = default)
    {
        ThrowIfDown();
        Interlocked.Increment(ref _textReads);
        if (BrokenRevisions.Contains(revId)) throw new CorpusUnavailableException($"fake: revision {revId} is unreadable");
        return Task.FromResult(Texts.GetValueOrDefault((pageId, revId)));
    }

    /// <summary>Adds the restored revision and one undone edit (<paramref name="revertedRevId"/>) with their texts.</summary>
    public FakeRevisionStore Edit(long pageId, long restoredRevId, long revertedRevId, string? user, string before, string after, string? comment = null)
    {
        var ts = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Rows.Add(new RevisionRow(pageId, restoredRevId, restoredRevId - 1, ts.AddMinutes(restoredRevId), "Someone", null, before.Length));
        Rows.Add(new RevisionRow(pageId, revertedRevId, restoredRevId, ts.AddMinutes(revertedRevId), user, comment, after.Length));
        Texts[(pageId, restoredRevId)] = before;
        Texts[(pageId, revertedRevId)] = after;
        return this;
    }

    private void ThrowIfDown()
    {
        if (Unavailable) throw new CorpusUnavailableException("fake revision store is down");
    }
}

/// <summary>
/// The real pipeline and stages over fakes: <see cref="FakeQueryEmbedder"/>, <see cref="FakeInvestigationCorpus"/>,
/// <see cref="FakeRevisionStore"/> and <see cref="FakeAnalysisModel"/>. Defaults = the Winter War corpus with two
/// removed edits on Talvisota: rev 1999 (adds a claim, by an IP) and rev 999 (a typo fix, not substantive).
/// </summary>
public sealed class PipelineKit
{
    public const string Before = "Talvisota alkoi 30. marraskuuta 1939, kun Neuvostoliitto hyökkäsi Suomeen.";
    public const string After = "Talvisota alkoi 30. marraskuuta 1939, kun Suomi hyökkäsi ensin Neuvostoliittoon Mainilassa.";
    public const string ClaimEn = "Finland attacked the Soviet Union first, at Mainila.";

    public FakeQueryEmbedder Embedder { get; } = new();
    public FakeInvestigationCorpus Corpus { get; } = FakeInvestigationCorpus.WithWinterWar();
    public FakeRevisionStore Revisions { get; } = new FakeRevisionStore()
        .Edit(1256, 1998, 1999, "192.0.2.1", Before, After, "korjaus")
        .Edit(1256, 998, 999, "192.0.2.1", Before, Before + " ");
    public FakeAnalysisModel Model { get; } = new FakeAnalysisModel().Returns("classify-edit", ClassificationJson());
    public InvestigationPipelineOptions Options { get; } = new();

    public static string ClassificationJson(string category = "false_factual_claim", int confidence = 4, string quote = "kun Suomi hyökkäsi ensin") => $$"""
        {"what_changed_en": "Rewrote who started the war.", "evidence": ["{{quote}}"],
         "claim_en": "{{ClaimEn}}", "claim_fi": "Suomi hyökkäsi ensin.", "narrative_en": "Soviet justification of the Winter War",
         "category": "{{category}}", "confidence": {{confidence}}, "historical_note_en": null, "uncertainty_note": null}
        """;

    public GeminiOptions Gemini { get; } = new();

    public InvestigationPipeline Build(IEditReader? reader = null)
    {
        var o = Microsoft.Extensions.Options.Options.Create(Options);
        return new InvestigationPipeline(
            new VectorTopicSearch(Embedder, Corpus, o),
            new RevertCandidateSource(Corpus, Revisions, o),
            reader ?? new CorpusEditReader(Revisions, o),
            new GeminiEditClassifier(Model, o, NullLogger<GeminiEditClassifier>.Instance),
            o, TimeProvider.System, NullLogger<InvestigationPipeline>.Instance);
    }

    /// <summary>The app's composition: run cache + reading cache on <paramref name="db"/> around the real pipeline.</summary>
    public CachingInvestigationRunner BuildCaching(TestDb db)
    {
        var o = Microsoft.Extensions.Options.Options.Create(Options);
        var reader = new CachedEditReader(new CorpusEditReader(Revisions, o), o, db.Factory, TimeProvider.System,
            NullLogger<CachedEditReader>.Instance);
        var gemini = Microsoft.Extensions.Options.Options.Create(Gemini);
        var fingerprint = new PipelineFingerprint(o, gemini, new PromptLibrary(gemini, NullLogger<PromptLibrary>.Instance));
        var cache = new InvestigationRunCache(db.Factory, fingerprint, TimeProvider.System, NullLogger<InvestigationRunCache>.Instance);
        return new CachingInvestigationRunner(Build(reader), cache, TimeProvider.System);
    }

    public void AddTo(IServiceCollection services) => services.AddSingleton<IInvestigationRunner>(Build());

    public Task<List<InvestigationEvent>> RunAsync(string topic = "Talvisota", CancellationToken ct = default) =>
        Collect(Build().RunAsync(topic, ct: ct));

    public static async Task<List<InvestigationEvent>> Collect(IAsyncEnumerable<InvestigationEvent> run)
    {
        var events = new List<InvestigationEvent>();
        await foreach (var e in run) events.Add(e);
        return events;
    }
}
