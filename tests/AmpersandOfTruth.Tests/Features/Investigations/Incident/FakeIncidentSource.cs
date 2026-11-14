using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.Investigations.Incident;

public sealed class FakeIncidentSource : IIncidentSource
{
    private readonly Dictionary<string, IReadOnlyList<IncidentView>?> _byTopic = new(StringComparer.OrdinalIgnoreCase);

    public TaskCompletionSource<IReadOnlyList<IncidentView>?>? Pending { get; set; }

    public int Calls { get; private set; }

    public FakeIncidentSource WithTopic(string topic, IReadOnlyList<IncidentView>? incidents)
    {
        _byTopic[topic] = incidents;
        return this;
    }

    public Task<IReadOnlyList<IncidentView>?> ForTopicAsync(string topicSlug, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;

        if (Pending is { } pending)
        {
            return pending.Task.WaitAsync(ct);
        }

        return Task.FromResult(_byTopic.TryGetValue(topicSlug, out var list) ? list : null);
    }

    public static IncidentView BuildIncident(
        string id = "ww-02",
        int rank = 2,
        EvidenceTier tier = EvidenceTier.Campaign,
        string? tierReason = null,
        string? claimEn = "Finland started the Winter War by shelling the village of Mainila.",
        string? claimOriginal = "Suomi aloitti talvisodan ampumalla Mainilan kylään.",
        string lang = "fi",
        IncidentKind kind = IncidentKind.Insertion,
        IReadOnlyList<ArticleRef>? articles = null,
        DateTimeOffset? firstSeen = null,
        DateTimeOffset? lastRemoved = null,
        TimeSpan? totalLive = null,
        int accountCount = 3,
        int insertCount = 4,
        IReadOnlyList<SignalView>? signals = null,
        IReadOnlyList<EditView>? edits = null,
        DiffPassage? passage = null,
        bool includePassage = true,
        AssessmentView? assessment = null,
        bool includeAssessment = true)
    {
        var t0 = firstSeen ?? new DateTimeOffset(2014, 3, 3, 14, 2, 0, TimeSpan.Zero);
        var resolvedArticles = articles ??
        [
            new ArticleRef(1256, "Talvisota", "Winter War"),
            new ArticleRef(2048, "Mainilan laukaukset", "Shelling of Mainila"),
        ];

        var resolvedSignals = signals ??
        [
            new SignalView(SignalCatalog.For("reinserted"), "Put back on 2 Feb 2015 after removal."),
            new SignalView(SignalCatalog.For("removal_reason"), "Editors noted unsourced wording."),
            new SignalView(SignalCatalog.For("multiple_articles"), "Also added to Mainilan laukaukset."),
        ];

        var resolvedEdits = edits ??
        [
            new EditView(
                RevId: 1001,
                ParentRevId: 1000,
                PageId: resolvedArticles[0].PageId,
                Timestamp: t0,
                Editor: EditorDisplay.For("Pohjantähti77"),
                Summary: "korjattu historiallinen fakta",
                Action: EditAction.Inserted,
                Live: TimeSpan.FromDays(4)),
            new EditView(
                RevId: 1002,
                ParentRevId: 1001,
                PageId: resolvedArticles[0].PageId,
                Timestamp: t0.AddDays(4).AddHours(-4).AddMinutes(39),
                Editor: EditorDisplay.For("Kumoaja"),
                Summary: "lähteetön väite",
                Action: EditAction.Removed,
                Live: null),
            new EditView(
                RevId: 1003,
                ParentRevId: 1002,
                PageId: resolvedArticles[0].PageId,
                Timestamp: new DateTimeOffset(2015, 2, 2, 22, 15, 0, TimeSpan.Zero),
                Editor: EditorDisplay.For("85.76.123.45"),
                Summary: null,
                Action: EditAction.Reinserted,
                Live: TimeSpan.FromDays(41)),
        ];

        var before = "Talvisota alkoi 30. marraskuuta 1939.";
        var after = "Talvisota alkoi 30. marraskuuta 1939. Suomi aloitti talvisodan ampumalla Mainilan kylään.";
        var resolvedPassage = includePassage
            ? (passage ?? new DiffPassage(1001, 1000, before, after, WikitextDiff.Compare(before, after)))
            : null;

        var resolvedAssessment = includeAssessment
            ? (assessment ?? new AssessmentView(
                Category: ContentCategory.FalseFactualClaim,
                Confidence: 3,
                Reasoning: "Replaces the account of the Mainila shots without adding a source.",
                Quotes: ["Suomi aloitti talvisodan ampumalla Mainilan kylään."],
                HasVerifiedQuote: true,
                Uncertainty: "Talk-page context is not visible."))
            : null;

        var record = new IncidentRecord
        {
            Id = id,
            PageId = resolvedArticles[0].PageId,
            InsertingRevIds = [1001],
            RemovingRevIds = [1002],
            Tier = tier,
            Score = 100 - rank,
        };

        return new IncidentView(
            Id: id,
            Rank: rank,
            Tier: tier,
            TierReason: tierReason ?? $"{EvidenceTiers.Get(tier).Label} \u2014 because: put back after removal, removal reason given.",
            ClaimEn: claimEn,
            ClaimOriginal: claimOriginal,
            Lang: lang,
            Kind: kind,
            Articles: resolvedArticles,
            FirstSeen: t0,
            LastRemoved: lastRemoved ?? new DateTimeOffset(2016, 1, 9, 10, 0, 0, TimeSpan.Zero),
            TotalLive: totalLive ?? TimeSpan.FromDays(61),
            AccountCount: accountCount,
            InsertCount: insertCount,
            Signals: resolvedSignals,
            Edits: resolvedEdits,
            Passage: resolvedPassage,
            Assessment: resolvedAssessment,
            Relevance: 0.95,
            ClusterId: null,
            Source: record);
    }

    public static IReadOnlyList<IncidentView> BuildSix(IncidentView? secondOverride = null) =>
    [
        BuildIncident(
            id: "ww-01",
            rank: 1,
            claimEn: "First incident claim.",
            articles: [new ArticleRef(1256, "Talvisota", "Winter War")],
            edits:
            [
                new EditView(901, 900, 1256, new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("OtherUser1"), "muokkaus", EditAction.Inserted, TimeSpan.FromDays(1)),
            ]),
        secondOverride ?? BuildIncident(id: "ww-02", rank: 2),
        BuildIncident(
            id: "ww-03",
            rank: 3,
            claimEn: "Third incident sharing editor.",
            articles: [new ArticleRef(3000, "Jatkosota", "Continuation War")],
            edits:
            [
                new EditView(1101, 1100, 3000, new DateTimeOffset(2015, 5, 1, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("Pohjantähti77"), "lisäys", EditAction.Inserted, TimeSpan.FromDays(2)),
            ]),
        BuildIncident(
            id: "ww-04",
            rank: 4,
            claimEn: "Fourth incident sharing second article.",
            articles: [new ArticleRef(2048, "Mainilan laukaukset", "Shelling of Mainila")],
            edits:
            [
                new EditView(1201, 1200, 2048, new DateTimeOffset(2015, 6, 1, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("UnrelatedA"), "lisäys", EditAction.Inserted, TimeSpan.FromDays(2)),
            ]),
        BuildIncident(
            id: "ww-05",
            rank: 5,
            claimEn: "Fifth incident also sharing article (should cap at 3).",
            articles: [new ArticleRef(1256, "Talvisota", "Winter War")],
            edits:
            [
                new EditView(1301, 1300, 1256, new DateTimeOffset(2015, 7, 1, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("UnrelatedB"), "lisäys", EditAction.Inserted, TimeSpan.FromDays(2)),
            ]),
        BuildIncident(
            id: "ww-06",
            rank: 6,
            claimEn: "Sixth completely unrelated incident.",
            articles: [new ArticleRef(9999, "Lapin sota", "Lapland War")],
            edits:
            [
                new EditView(1401, 1400, 9999, new DateTimeOffset(2015, 8, 1, 0, 0, 0, TimeSpan.Zero), EditorDisplay.For("UnrelatedC"), "lisäys", EditAction.Inserted, TimeSpan.FromDays(2)),
            ]),
    ];
}
