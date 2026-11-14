using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

// The UI's read model of one incident (#98 §1, docs/design/plan/4-build-order.md). Built by IncidentMapper from the
// contract record (IncidentRecord) plus what the app derives itself from the corpus: passage, titles, editor display.
// Pages and components bind to these records only — never to IncidentRecord — so the pipeline can change underneath.

public enum IncidentKind { Insertion, Deletion, CitationSwap, NumberChange }

/// <param name="TitleFi">The fi.wikipedia.org title (never null; "Page {id}" if unknown).</param>
/// <param name="TitleEn">The English sibling's title via <c>wikidata_id</c>, when one exists. Not a translation.</param>
public sealed record ArticleRef(long PageId, string TitleFi, string? TitleEn)
{
    public Uri Url => WikipediaLinks.Article(TitleFi);
}

/// <param name="Phrase">Wording from <see cref="SignalCatalog"/> — the only place chips/sentences come from.</param>
/// <param name="Evidence">The incident's own evidence sentence for this signal (plain text, may be Finnish-free English).</param>
public sealed record SignalView(SignalPhrase Phrase, string? Evidence)
{
    public string Id => Phrase.Id;
}

public enum EditAction { Inserted, Removed, Reinserted }

/// <param name="Editor">From <see cref="EditorDisplay.For"/> on the raw corpus user (IPs masked).</param>
/// <param name="Summary">The edit summary through <see cref="EditorDisplay.MaskComment"/>; Finnish, untranslated.</param>
/// <param name="Live">How long the text this edit added stayed live (insert/re-insert rows only).</param>
public sealed record EditView(
    long RevId, long? ParentRevId, long PageId, DateTimeOffset Timestamp, EditorLabel Editor,
    string? Summary, EditAction Action, TimeSpan? Live)
{
    public Uri DiffUrl => WikipediaLinks.Diff(RevId);
}

/// <summary>
/// What changed in the first inserting revision versus its parent. <see cref="Diff"/> = <c>WikitextDiff.Compare(Before, After)</c>.
/// All text here is raw Wikipedia content: render it as encoded text, never as <c>MarkupString</c>.
/// </summary>
public sealed record DiffPassage(long RevId, long? ParentRevId, string BeforeWikitext, string AfterWikitext, WikitextDiffResult Diff)
{
    private WikitextProseView? _before, _after;
    public string BeforeProse => (_before ??= WikitextProse.Of(BeforeWikitext)).Prose;
    public string AfterProse => (_after ??= WikitextProse.Of(AfterWikitext)).Prose;
}

/// <param name="Confidence">1 (guess) – 5 (certain), about the category.</param>
/// <param name="Reasoning">English explanation of what changed (model output).</param>
/// <param name="Quotes">Verbatim spans of the edit the model relied on; highlighted in the diff.</param>
public sealed record AssessmentView(
    ContentCategory Category, int Confidence, string? Reasoning, IReadOnlyList<string> Quotes, bool HasVerifiedQuote,
    string? Uncertainty);

/// <param name="Id">Stable id, used in the URL <c>/investigate/{topic}/incidents/{Id}</c>.</param>
/// <param name="Rank">1-based position in the topic's ranked list (prev/next).</param>
/// <param name="TierReason">The "because …" sentence (<see cref="TierAssessment.BecauseSentence"/> wording).</param>
/// <param name="ClaimEn">English headline; null → the UI shows a neutral "Text added to {article}" line.</param>
/// <param name="ClaimOriginal">The Finnish original of the claim, shown under the headline.</param>
/// <param name="Passage">null when the corpus text isn't available (corpus down) — the diff block shows a banner.</param>
/// <param name="Assessment">null = no automated assessment (LLM down / over budget) → the "unavailable" state.</param>
/// <param name="Source">The contract record this was built from (rev ids, raw fields).</param>
public sealed record IncidentView(
    string Id,
    int Rank,
    EvidenceTier Tier,
    string TierReason,
    string? ClaimEn,
    string? ClaimOriginal,
    string Lang,
    IncidentKind Kind,
    IReadOnlyList<ArticleRef> Articles,
    DateTimeOffset FirstSeen,
    DateTimeOffset? LastRemoved,
    TimeSpan TotalLive,
    int AccountCount,
    int InsertCount,
    IReadOnlyList<SignalView> Signals,
    IReadOnlyList<EditView> Edits,
    DiffPassage? Passage,
    AssessmentView? Assessment,
    double Relevance,
    string? ClusterId,
    IncidentRecord Source)
{
    public EvidenceTierInfo TierInfo => EvidenceTiers.Get(Tier);
    public ArticleRef PrimaryArticle => Articles[0];
}
