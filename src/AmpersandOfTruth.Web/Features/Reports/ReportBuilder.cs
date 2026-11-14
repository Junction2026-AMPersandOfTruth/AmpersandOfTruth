using System.Globalization;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.Overview;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Library;

namespace AmpersandOfTruth.Web.Features.Reports;

public sealed record ReportOptions(bool PinnedOnly, bool IncludeSummary);

public sealed record ReportCoverage(
    int ArticleCount,
    string DatasetPeriod,
    DateTimeOffset? EarliestEdit,
    DateTimeOffset? LatestEdit);

public sealed record ReportExecutiveSummary(
    IReadOnlyDictionary<EvidenceTier, int> TierCounts,
    string? AutomatedSummary);

public sealed record ReportEdit(
    long RevId,
    DateTimeOffset Timestamp,
    EditorLabel Editor,
    EditAction Action,
    string ActionLabel,
    string? Summary,
    TimeSpan? Live,
    Uri? RevisionUrl);

public sealed record ReportPassage(
    string BeforeProse,
    string AfterProse,
    IReadOnlyList<IReadOnlyList<DiffToken>> Paragraphs);

public sealed record ReportIncident(
    string Id,
    int Rank,
    bool IsPinned,
    string? PinNote,
    string Headline,
    string? ClaimOriginal,
    bool IsMachineTranslatedHeadline,
    string Lang,
    EvidenceTier Tier,
    string TierReason,
    IReadOnlyList<ArticleRef> Articles,
    DateTimeOffset FirstSeen,
    DateTimeOffset? LastRemoved,
    TimeSpan TotalLive,
    string TotalLiveText,
    IReadOnlyList<ReportEdit> Edits,
    ReportPassage? Passage)
{
    public EvidenceTierInfo TierInfo => EvidenceTiers.Get(Tier);
}

public sealed record CitedRevision(long RevId, Uri Url);

public sealed record ReportAppendix(
    string MethodologySummary,
    IReadOnlyList<EvidenceTierInfo> Levels,
    IReadOnlyList<string> Limitations,
    string AttributionText,
    string MachineTranslationNotice,
    string License,
    Uri LicenseUrl,
    IReadOnlyList<CitedRevision> CitedRevisions);

public sealed record InvestigationReport(
    Guid SavedId,
    string Title,
    string Topic,
    string TopicSlug,
    DateTimeOffset GeneratedAt,
    DateTimeOffset AnalysedAt,
    ReportCoverage Coverage,
    bool PinnedOnly,
    int TotalIncidentCount,
    int PinnedIncidentCount,
    ReportExecutiveSummary ExecutiveSummary,
    IReadOnlyList<ReportIncident> Incidents,
    ReportAppendix Appendix);

public sealed class ReportBuilder(
    LibraryStore store,
    IncidentMapper mapper,
    IAnalysisModel analysisModel,
    TimeProvider time)
{
    public const string DatasetPeriodText = "2003 – 1 Oct 2026";

    private static readonly string ParagraphJoin = new([(char)10, (char)10]);

    private static readonly IReadOnlyList<EvidenceTierInfo> OrderedLevels =
    [
        EvidenceTiers.Campaign,
        EvidenceTiers.Contested,
        EvidenceTiers.Reverted,
    ];

    public async Task<InvestigationReport?> BuildAsync(
        Guid savedId,
        ReportOptions opts,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);

        var loaded = await store.GetAsync(savedId, ct);
        if (loaded is null)
        {
            return null;
        }

        var (summary, snapshot) = loaded.Value;
        var pins = await store.PinsAsync(savedId, ct);
        var pinMap = new Dictionary<string, PinnedIncident>(StringComparer.Ordinal);
        foreach (var pin in pins)
        {
            pinMap[pin.IncidentId] = pin;
        }

        var totalIncidentCount = snapshot.Incidents.Count;
        var pinnedIncidentCount = 0;
        var mappedIncidents = new List<(ReportIncident Incident, IncidentRecord Source)>();

        for (var i = 0; i < snapshot.Incidents.Count; i++)
        {
            var record = snapshot.Incidents[i];
            var resolvedId = ResolveIncidentId(record);
            var isPinned = pinMap.TryGetValue(resolvedId, out var pin);
            if (isPinned)
            {
                pinnedIncidentCount++;
            }

            if (opts.PinnedOnly && !isPinned)
            {
                continue;
            }

            var view = await mapper.MapAsync(record, i + 1, ct);
            if (!isPinned && pinMap.TryGetValue(view.Id, out var viewPin))
            {
                isPinned = true;
                pin = viewPin;
            }

            if (opts.PinnedOnly && !isPinned)
            {
                continue;
            }

            mappedIncidents.Add((BuildReportIncident(view, isPinned, pin?.Note), record));
        }

        var orderedPairs = mappedIncidents
            .OrderByDescending(static x => x.Incident.IsPinned)
            .ThenBy(static x => x.Incident.Rank)
            .ThenBy(static x => x.Incident.Id, StringComparer.Ordinal)
            .ToList();

        var reportIncidents = orderedPairs.Select(static x => x.Incident).ToList();

        var tierCounts = CountTiers(reportIncidents);
        string? automatedSummary = null;

        if (opts.IncludeSummary && reportIncidents.Count > 0)
        {
            var topicName = !string.IsNullOrWhiteSpace(summary.Topic) ? summary.Topic : snapshot.Topic;
            var promptItems = reportIncidents
                .Select(static inc => new SummarizeIncidentItem(
                    Tier: inc.TierInfo.Label,
                    ClaimEn: inc.Headline,
                    Article: FormatArticlesPlain(inc.Articles),
                    FirstSeen: inc.FirstSeen.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LastRemoved: inc.LastRemoved?.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    LiveDuration: inc.TotalLiveText))
                .ToList();

            var vars = SummarizeInvestigationPrompt.Variables(topicName, promptItems);
            var result = await analysisModel.RunAsync<ReportSummary>(
                new AnalysisRequest(SummarizeInvestigationPrompt.Id, vars) { Scope = savedId.ToString() },
                ct);

            if (result.Outcome == AnalysisOutcome.Ok && !string.IsNullOrWhiteSpace(result.Value?.Summary))
            {
                automatedSummary = result.Value.Summary.Trim();
            }
        }

        var coverage = BuildCoverage(snapshot, reportIncidents);
        var citedRevisions = CollectCitedRevisions(orderedPairs);

        var appendix = new ReportAppendix(
            MethodologySummary: Evidence.Methodology.Summary,
            Levels: OrderedLevels,
            Limitations: Evidence.Methodology.Limitations,
            AttributionText: Attribution.FooterText,
            MachineTranslationNotice: Attribution.MachineTranslationNotice,
            License: Attribution.License,
            LicenseUrl: Attribution.LicenseUrl,
            CitedRevisions: citedRevisions);

        var topic = !string.IsNullOrWhiteSpace(summary.Topic) ? summary.Topic : snapshot.Topic;
        var slug = !string.IsNullOrWhiteSpace(summary.TopicSlug)
            ? summary.TopicSlug
            : TopicSlug.From(topic);
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = "investigation";
        }

        var analysedAt = snapshot.AnalysedAt != default ? snapshot.AnalysedAt.ToUniversalTime() : summary.UpdatedAt.ToUniversalTime();

        return new InvestigationReport(
            SavedId: summary.Id,
            Title: summary.Title,
            Topic: topic,
            TopicSlug: slug,
            GeneratedAt: time.GetUtcNow().ToUniversalTime(),
            AnalysedAt: analysedAt,
            Coverage: coverage,
            PinnedOnly: opts.PinnedOnly,
            TotalIncidentCount: totalIncidentCount,
            PinnedIncidentCount: pinnedIncidentCount,
            ExecutiveSummary: new ReportExecutiveSummary(tierCounts, automatedSummary),
            Incidents: reportIncidents,
            Appendix: appendix);
    }

    internal static string FormatArticlesPlain(IReadOnlyList<ArticleRef> articles)
    {
        if (articles.Count == 0)
        {
            return "article";
        }

        return string.Join(", ", articles.Select(static a =>
            !string.IsNullOrWhiteSpace(a.TitleEn) && !string.Equals(a.TitleEn, a.TitleFi, StringComparison.OrdinalIgnoreCase)
                ? $"{a.TitleFi} ({a.TitleEn})"
                : a.TitleFi));
    }

    internal static string FormatCoverageLine(ReportCoverage coverage)
    {
        var articleWord = coverage.ArticleCount == 1 ? "article" : "articles";
        var countText = coverage.ArticleCount.ToString("N0", CultureInfo.InvariantCulture);

        if (coverage.EarliestEdit is { } earliest && coverage.LatestEdit is { } latest)
        {
            var start = earliest.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            var end = latest.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            var span = string.Equals(start, end, StringComparison.Ordinal) ? start : $"{start} – {end}";
            return $"{countText} Finnish Wikipedia {articleWord} · edit history {coverage.DatasetPeriod} (findings: {span})";
        }

        return $"{countText} Finnish Wikipedia {articleWord} · edit history {coverage.DatasetPeriod}";
    }

    private static string ResolveIncidentId(IncidentRecord r)
    {
        var firstRevId = r.InsertingRevIds.Count > 0 ? r.InsertingRevIds[0] : 0L;
        return !string.IsNullOrWhiteSpace(r.Id) ? r.Id : $"{r.PageId}-{firstRevId}";
    }

    private static ReportIncident BuildReportIncident(IncidentView view, bool isPinned, string? pinNote)
    {
        var hasClaimEn = !string.IsNullOrWhiteSpace(view.ClaimEn);
        var headline = FormatHeadline(view);
        var normalizedOriginal = string.IsNullOrWhiteSpace(view.ClaimOriginal) ? null : view.ClaimOriginal.Trim();
        var normalizedNote = string.IsNullOrWhiteSpace(pinNote) ? null : pinNote.Trim();
        var lang = string.IsNullOrWhiteSpace(view.Lang) ? "fi" : view.Lang.Trim();

        var edits = view.Edits
            .Select(static e => new ReportEdit(
                RevId: e.RevId,
                Timestamp: e.Timestamp.ToUniversalTime(),
                Editor: e.Editor,
                Action: e.Action,
                ActionLabel: FormatAction(e.Action),
                Summary: string.IsNullOrWhiteSpace(e.Summary) ? null : e.Summary.Trim(),
                Live: e.Live,
                RevisionUrl: e.RevId > 0 ? WikipediaLinks.Revision(e.RevId) : null))
            .ToList();

        return new ReportIncident(
            Id: view.Id,
            Rank: view.Rank,
            IsPinned: isPinned,
            PinNote: normalizedNote,
            Headline: headline,
            ClaimOriginal: normalizedOriginal,
            IsMachineTranslatedHeadline: hasClaimEn,
            Lang: lang,
            Tier: view.Tier,
            TierReason: view.TierReason,
            Articles: view.Articles,
            FirstSeen: view.FirstSeen.ToUniversalTime(),
            LastRemoved: view.LastRemoved?.ToUniversalTime(),
            TotalLive: view.TotalLive,
            TotalLiveText: OverviewState.Duration(view.TotalLive),
            Edits: edits,
            Passage: BuildPassage(view.Passage));
    }

    private static string FormatHeadline(IncidentView incident)
    {
        if (!string.IsNullOrWhiteSpace(incident.ClaimEn))
        {
            var claim = incident.ClaimEn.Trim();
            if (incident.Kind == IncidentKind.Deletion)
            {
                return claim.StartsWith("Removed:", StringComparison.OrdinalIgnoreCase)
                    ? claim
                    : $"Removed: {claim}";
            }

            return claim;
        }

        var articleTitle = incident.Articles.Count > 0
            ? (incident.PrimaryArticle.TitleEn ?? incident.PrimaryArticle.TitleFi)
            : "article";

        return incident.Kind == IncidentKind.Deletion
            ? $"Removed: text from {articleTitle}"
            : $"Text added to {articleTitle}";
    }

    private static string FormatAction(EditAction action) => action switch
    {
        EditAction.Inserted => "Added",
        EditAction.Removed => "Removed",
        EditAction.Reinserted => "Put back",
        _ => "Added",
    };

    private static ReportPassage? BuildPassage(DiffPassage? passage)
    {
        if (passage is null)
        {
            return null;
        }

        var model = DiffModel.Build(passage, DiffTextMode.Prose);
        var changedBlocks = model.Blocks.Where(static b => b.IsChanged).ToList();

        if (changedBlocks.Count > 0)
        {
            var paragraphs = changedBlocks.Select(static b => b.Tokens).ToList();
            var beforeProse = string.Join(ParagraphJoin, changedBlocks.Select(static b => b.BeforeText.Trim()).Where(static s => s.Length > 0));
            var afterProse = string.Join(ParagraphJoin, changedBlocks.Select(static b => b.AfterText.Trim()).Where(static s => s.Length > 0));
            return new ReportPassage(beforeProse, afterProse, paragraphs);
        }

        var fallbackBefore = passage.BeforeProse.Trim();
        var fallbackAfter = passage.AfterProse.Trim();
        if (fallbackBefore.Length == 0 && fallbackAfter.Length == 0)
        {
            fallbackBefore = (passage.BeforeWikitext ?? "").Trim();
            fallbackAfter = (passage.AfterWikitext ?? "").Trim();
        }

        var tokens = new List<DiffToken>(2);
        if (string.Equals(fallbackBefore, fallbackAfter, StringComparison.Ordinal))
        {
            if (fallbackAfter.Length > 0)
            {
                tokens.Add(new DiffToken(fallbackAfter, DiffTokenKind.Same));
            }
        }
        else
        {
            if (fallbackBefore.Length > 0)
            {
                tokens.Add(new DiffToken(fallbackBefore, DiffTokenKind.Removed));
            }

            if (fallbackAfter.Length > 0)
            {
                tokens.Add(new DiffToken(fallbackAfter, DiffTokenKind.Added));
            }
        }

        return new ReportPassage(
            fallbackBefore,
            fallbackAfter,
            tokens.Count > 0 ? [tokens] : []);
    }

    private static Dictionary<EvidenceTier, int> CountTiers(IReadOnlyList<ReportIncident> incidents)
    {
        var counts = new Dictionary<EvidenceTier, int>
        {
            [EvidenceTier.Campaign] = 0,
            [EvidenceTier.Contested] = 0,
            [EvidenceTier.Reverted] = 0,
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

    private static ReportCoverage BuildCoverage(SavedSnapshot snapshot, IReadOnlyList<ReportIncident> incidents)
    {
        var pageIds = new HashSet<long>();
        foreach (var article in snapshot.Articles)
        {
            if (article.PageId > 0)
            {
                pageIds.Add(article.PageId);
            }
        }

        if (pageIds.Count == 0)
        {
            foreach (var rec in snapshot.Incidents)
            {
                if (rec.PageId > 0)
                {
                    pageIds.Add(rec.PageId);
                }

                foreach (var otherId in rec.OtherPageIds)
                {
                    if (otherId > 0)
                    {
                        pageIds.Add(otherId);
                    }
                }
            }
        }

        DateTimeOffset? earliest = null;
        DateTimeOffset? latest = null;

        foreach (var inc in incidents)
        {
            TrackTimestamp(inc.FirstSeen, ref earliest, ref latest);
            if (inc.LastRemoved is { } lr)
            {
                TrackTimestamp(lr, ref earliest, ref latest);
            }

            foreach (var edit in inc.Edits)
            {
                TrackTimestamp(edit.Timestamp, ref earliest, ref latest);
            }
        }

        return new ReportCoverage(pageIds.Count, DatasetPeriodText, earliest, latest);
    }

    private static void TrackTimestamp(DateTimeOffset ts, ref DateTimeOffset? earliest, ref DateTimeOffset? latest)
    {
        if (ts <= DateTimeOffset.UnixEpoch)
        {
            return;
        }

        if (earliest is null || ts < earliest.Value)
        {
            earliest = ts;
        }

        if (latest is null || ts > latest.Value)
        {
            latest = ts;
        }
    }

    private static IReadOnlyList<CitedRevision> CollectCitedRevisions(
        IReadOnlyList<(ReportIncident Incident, IncidentRecord Source)> pairs)
    {
        var ids = new SortedSet<long>();
        foreach (var (incident, source) in pairs)
        {
            foreach (var edit in incident.Edits)
            {
                if (edit.RevId > 0)
                {
                    ids.Add(edit.RevId);
                }
            }

            foreach (var revId in source.InsertingRevIds)
            {
                if (revId > 0)
                {
                    ids.Add(revId);
                }
            }

            foreach (var revId in source.RemovingRevIds)
            {
                if (revId > 0)
                {
                    ids.Add(revId);
                }
            }
        }

        return ids.Select(static id => new CitedRevision(id, WikipediaLinks.Revision(id))).ToList();
    }
}
