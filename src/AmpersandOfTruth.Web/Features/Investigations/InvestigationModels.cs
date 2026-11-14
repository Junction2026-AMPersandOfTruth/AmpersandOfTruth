namespace AmpersandOfTruth.Web.Features.Investigations;

using AmpersandOfTruth.Web.Features.Evidence;

/// <summary>
/// An article close to the topic in bge-m3 space. <see cref="Distance"/> = cosine distance (0 = identical).
/// <see cref="ViaLang"/> = the language whose vector matched, when an en/sv hit was folded onto this fi article (R1).
/// </summary>
public sealed record TopicArticle(string Lang, long PageId, string Title, double Distance, string? WikidataId = null, string? ViaLang = null)
{
    public string Url => $"https://{Lang}.wikipedia.org/?curid={PageId}";

    /// <summary>Only fi articles have revision history in the corpus (dataset M).</summary>
    public bool HasHistory => Lang == "fi";

    /// <summary>Found by an exact (or prefix) match of an LLM title guess (R3-D), not only by vector distance. A title-only
    /// hit has no vector distance of its own and carries 1 (the far end of the cosine range).</summary>
    public bool TitleMatch { get; init; }
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
    /// <summary>Bytes the undone span added (last undone revision minus the restored content); ≤ 0 = it took text out.
    /// Stage 2's stand-in for prose length (P1.S3 formula 3) before any diff is read.</summary>
    public int UndoneBytes { get; init; }

    /// <summary>When the first undone revision was saved (the span's start). Null when unknown.</summary>
    public DateTime? FirstUndoneAt { get; init; }

    /// <summary>Later revisions with the exact content of the last undone revision: the whole span put back. Stage 2's
    /// stand-in for re-insertions; stage 3 counts them properly (<c>EditReading.Reinsertions</c>).</summary>
    public int Reappearances { get; init; }

    public Uri DiffUrl => WikipediaLinks.Diff(RevId);                  // the revert itself: diff=prev&oldid=RevId
    public Uri RevertedDiffUrl => WikipediaLinks.Diff(RevertedRevId);  // the last undone edit: diff=prev&oldid=RevertedRevId
}
