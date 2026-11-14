namespace AmpersandOfTruth.Web.Features.Investigations;

/// <summary>An article close to the topic in bge-m3 space. <see cref="Distance"/> = cosine distance (0 = identical).</summary>
public sealed record TopicArticle(string Lang, long PageId, string Title, double Distance)
{
    public string Url => WikiLinks.Article(Lang, PageId);

    /// <summary>Only fi articles have revision history in the corpus (dataset M).</summary>
    public bool HasHistory => Lang == "fi";
}

/// <summary>
/// An identity revert: revision <see cref="RevId"/> restored the exact content (same sha1) of the earlier,
/// non-adjacent revision <see cref="RestoredRevId"/>, undoing <see cref="RevertedCount"/> edits in between.
/// <see cref="RevertedRevId"/>/<see cref="RevertedUser"/> = the last of those undone edits (the one just before the revert).
/// </summary>
public sealed record RevertEvent(
    long PageId,
    long RevId,
    DateTime Timestamp,
    string? User,
    string? Comment,
    long RestoredRevId,
    int RevertedCount,
    long RevertedRevId,
    string? RevertedUser)
{
    public string RevisionUrl => WikiLinks.Revision("fi", RevId);
    public string DiffUrl => WikiLinks.Diff("fi", RevertedRevId, RevId);
    public string RevertedRevisionUrl => WikiLinks.Revision("fi", RevertedRevId);
}

public sealed record ArticleReverts(TopicArticle Article, IReadOnlyList<RevertEvent> Reverts);

public enum InvestigationStatus { Ok, EmbeddingUnavailable, CorpusUnavailable }

public sealed record InvestigationResult(
    InvestigationStatus Status, string Topic, IReadOnlyList<ArticleReverts> Articles, string? Error = null)
{
    public static InvestigationResult Unavailable(InvestigationStatus status, string topic, string error) =>
        new(status, topic, [], error);
}

public static class WikiLinks
{
    public static string Article(string lang, long pageId) => $"https://{lang}.wikipedia.org/?curid={pageId}";

    public static string Revision(string lang, long revId) => $"https://{lang}.wikipedia.org/w/index.php?oldid={revId}";

    /// <summary>What the revert removed: diff from <paramref name="fromRevId"/> to <paramref name="toRevId"/>.</summary>
    public static string Diff(string lang, long fromRevId, long toRevId) =>
        $"https://{lang}.wikipedia.org/w/index.php?diff={toRevId}&oldid={fromRevId}";
}
