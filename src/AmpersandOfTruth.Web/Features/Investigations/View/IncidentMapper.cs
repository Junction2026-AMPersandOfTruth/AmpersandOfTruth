using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.Pipeline;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

/// <summary>
/// Builds the UI's <see cref="IncidentView"/> from a contract <see cref="IncidentRecord"/> plus corpus metadata and wikitext.
/// Never throws when the corpus is unavailable: each corpus lookup degrades independently to the record's own fields.
/// </summary>
public sealed partial class IncidentMapper(IIncidentCorpus corpus, IRevisionStore revisions)
{
    private readonly ConcurrentDictionary<(string Id, long FirstRevId), Lazy<Task<IncidentView>>> _cache = new();

    public async Task<IncidentView> MapAsync(IncidentRecord r, int rank, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(r);
        ct.ThrowIfCancellationRequested();

        var firstRevId = r.InsertingRevIds.Count > 0 ? r.InsertingRevIds[0] : 0L;
        var id = !string.IsNullOrWhiteSpace(r.Id) ? r.Id : $"{r.PageId}-{firstRevId}";
        var key = (id, firstRevId);

        var lazy = _cache.GetOrAdd(key, _ => CreateEntry(key, r, firstRevId, id, rank));
        var view = await lazy.Value.WaitAsync(ct);
        return view.Rank == rank ? view : view with { Rank = rank };
    }

    private Lazy<Task<IncidentView>> CreateEntry(
        (string Id, long FirstRevId) key,
        IncidentRecord r,
        long firstRevId,
        string id,
        int rank)
    {
        Lazy<Task<IncidentView>>? lazy = null;
        lazy = new Lazy<Task<IncidentView>>(
            async () =>
            {
                try
                {
                    var view = await BuildViewAsync(r, firstRevId, id, rank, CancellationToken.None);
                    if (view.Passage is null && lazy is not null)
                    {
                        _cache.TryRemove(KeyValuePair.Create(key, lazy));
                    }

                    return view;
                }
                catch
                {
                    if (lazy is not null)
                    {
                        _cache.TryRemove(KeyValuePair.Create(key, lazy));
                    }

                    throw;
                }
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
        return lazy;
    }

    private async Task<IncidentView> BuildViewAsync(
        IncidentRecord r,
        long firstRevId,
        string id,
        int rank,
        CancellationToken ct)
    {
        var articlesTask = ResolveArticlesAsync(r, ct);
        var revisionsTask = ResolveRevisionsAsync(r, firstRevId, ct);
        await Task.WhenAll(articlesTask, revisionsTask);

        var articles = await articlesTask;
        var revisionRows = await revisionsTask;
        var passage = await ResolvePassageAsync(r, firstRevId, revisionRows, ct);
        var kind = DeriveKind(passage);
        var edits = BuildEdits(r, revisionRows);

        var firstSeen = r.FirstInsertedAt?.ToUniversalTime()
            ?? (edits.Count > 0 ? edits[0].Timestamp : DateTimeOffset.UnixEpoch);
        var lastRemoved = r.LastRemovedAt?.ToUniversalTime()
            ?? edits.Where(e => e.Action == EditAction.Removed).Select(e => (DateTimeOffset?)e.Timestamp).LastOrDefault();
        var totalLive = r.TotalExposureSeconds is { } exposureSecs
            ? TimeSpan.FromSeconds(exposureSecs)
            : edits.Where(e => e.Live is not null).Aggregate(TimeSpan.Zero, (sum, e) => sum + e.Live!.Value);

        var insertEdits = edits.Where(e => e.Action is EditAction.Inserted or EditAction.Reinserted).ToList();
        var accountCount = Math.Max(1, insertEdits.Select(e => e.Editor.GroupingKey).Distinct(StringComparer.Ordinal).Count());
        var insertCount = Math.Max(1, insertEdits.Count);

        var signals = r.Signals
            .Select(s => new SignalView(SignalCatalog.For(s.Kind, s.Label), s.Evidence))
            .ToList();

        var tierReason = !string.IsNullOrWhiteSpace(r.TierReason)
            ? r.TierReason
            : BuildFallbackTierReason(r.Tier, signals);

        var assessment = r.Assessment is { } a
            ? new AssessmentView(
                a.Category,
                a.Confidence,
                a.WhatChangedEn ?? a.HistoricalNoteEn,
                a.Quotes,
                a.HasVerifiedQuote,
                a.UncertaintyNote)
            : null;

        return new IncidentView(
            Id: id,
            Rank: rank,
            Tier: r.Tier,
            TierReason: tierReason,
            ClaimEn: r.ClaimEn,
            ClaimOriginal: r.ClaimFi,
            Lang: "fi",
            Kind: kind,
            Articles: articles,
            FirstSeen: firstSeen,
            LastRemoved: lastRemoved,
            TotalLive: totalLive,
            AccountCount: accountCount,
            InsertCount: insertCount,
            Signals: signals,
            Edits: edits,
            Passage: passage,
            Assessment: assessment,
            Relevance: r.Score,
            ClusterId: string.IsNullOrWhiteSpace(r.ClusterId) ? null : r.ClusterId,
            Source: r);
    }

    private async Task<IReadOnlyList<ArticleRef>> ResolveArticlesAsync(IncidentRecord r, CancellationToken ct)
    {
        var pageIds = new List<long> { r.PageId };
        foreach (var pid in r.OtherPageIds)
        {
            if (!pageIds.Contains(pid))
            {
                pageIds.Add(pid);
            }
        }

        IReadOnlyDictionary<long, ArticleRef> fromCorpus;
        try
        {
            fromCorpus = await corpus.ArticlesAsync(pageIds, ct);
        }
        catch (CorpusUnavailableException)
        {
            fromCorpus = new Dictionary<long, ArticleRef>();
        }

        var list = new List<ArticleRef>(pageIds.Count);
        foreach (var pid in pageIds)
        {
            var fallback = r.Articles.FirstOrDefault(a => a.PageId == pid);
            if (fromCorpus.TryGetValue(pid, out var hit))
            {
                list.Add(new ArticleRef(pid, hit.TitleFi, hit.TitleEn ?? NullIfBlank(fallback?.TitleEn)));
            }
            else
            {
                list.Add(new ArticleRef(
                    pid,
                    NullIfBlank(fallback?.TitleFi) ?? $"Page {pid}",
                    NullIfBlank(fallback?.TitleEn)));
            }
        }

        return list;
    }

    private async Task<Dictionary<(long PageId, long RevId), RevisionRow>> ResolveRevisionsAsync(
        IncidentRecord r, long firstRevId, CancellationToken ct)
    {
        var needed = new HashSet<(long PageId, long RevId)>();
        if (firstRevId > 0)
        {
            var firstEdit = r.Edits.FirstOrDefault(e => e.RevId == firstRevId);
            var firstPage = firstEdit is { PageId: > 0 } ? firstEdit.PageId : r.PageId;
            needed.Add((firstPage, firstRevId));
        }

        if (r.Edits.Count > 0)
        {
            foreach (var e in r.Edits)
            {
                var pid = e.PageId > 0 ? e.PageId : r.PageId;
                if (e.RevId > 0) needed.Add((pid, e.RevId));
            }
        }
        else
        {
            foreach (var revId in r.InsertingRevIds.Concat(r.RemovingRevIds))
            {
                if (revId > 0) needed.Add((r.PageId, revId));
            }
        }

        var groups = needed.GroupBy(x => x.PageId, x => x.RevId).ToList();
        var groupTasks = new Task<IReadOnlyList<RevisionRow>>[groups.Count];
        for (var i = 0; i < groups.Count; i++)
        {
            groupTasks[i] = FetchRevisionGroupAsync(groups[i].Key, groups[i].ToList(), ct);
        }

        var fetchedGroups = await Task.WhenAll(groupTasks);
        var rows = new Dictionary<(long PageId, long RevId), RevisionRow>(needed.Count);
        foreach (var fetched in fetchedGroups)
        {
            foreach (var row in fetched)
            {
                rows[(row.PageId, row.RevId)] = row;
            }
        }

        return rows;
    }

    private async Task<IReadOnlyList<RevisionRow>> FetchRevisionGroupAsync(
        long pageId, IReadOnlyCollection<long> revIds, CancellationToken ct)
    {
        try
        {
            return await corpus.RevisionsAsync(pageId, revIds, ct);
        }
        catch (CorpusUnavailableException)
        {
            // Degrade gracefully to the fields on IncidentRecord.
            return [];
        }
    }

    private async Task<DiffPassage?> ResolvePassageAsync(
        IncidentRecord r,
        long firstRevId,
        Dictionary<(long PageId, long RevId), RevisionRow> revisionRows,
        CancellationToken ct)
    {
        if (firstRevId <= 0)
        {
            return null;
        }

        var firstEdit = r.Edits.FirstOrDefault(e => e.RevId == firstRevId);
        var pageId = firstEdit is { PageId: > 0 } ? firstEdit.PageId : r.PageId;
        revisionRows.TryGetValue((pageId, firstRevId), out var firstRow);

        var parentRevId = NormalizeRevId(firstRow?.ParentRevId) ?? NormalizeRevId(firstEdit?.ParentRevId);

        try
        {
            string? revText;
            string? parentText = null;
            if (parentRevId is { } pid)
            {
                var revTextTask = revisions.TextAsync(pageId, firstRevId, ct);
                var parentTextTask = revisions.TextAsync(pageId, pid, ct);
                await Task.WhenAll(revTextTask, parentTextTask);

                revText = await revTextTask;
                parentText = await parentTextTask;
                if (revText is null || parentText is null)
                {
                    return null;
                }
            }
            else
            {
                revText = await revisions.TextAsync(pageId, firstRevId, ct);
                if (revText is null)
                {
                    return null;
                }
            }

            return new DiffPassage(
                RevId: firstRevId,
                ParentRevId: parentRevId,
                BeforeWikitext: parentText ?? "",
                AfterWikitext: revText,
                Diff: WikitextDiff.Compare(parentText, revText));
        }
        catch (CorpusUnavailableException)
        {
            return null;
        }
    }

    internal static IncidentKind DeriveKind(DiffPassage? passage)
    {
        if (passage is null)
        {
            return IncidentKind.Insertion;
        }

        var diff = passage.Diff;
        if (diff.Stats.AddedRefs > 0 && diff.Stats.RemovedRefs > 0)
        {
            return IncidentKind.CitationSwap;
        }

        if (HasDigitOnlyChangedPair(diff.Segments))
        {
            return IncidentKind.NumberChange;
        }

        if (diff.Stats.RemovedProseChars > diff.Stats.AddedProseChars && diff.Stats.AddedProseChars < 20)
        {
            return IncidentKind.Deletion;
        }

        return IncidentKind.Insertion;
    }

    private const int ShortNumberSegmentMaxChars = 32;

    private static bool HasDigitOnlyChangedPair(IReadOnlyList<DiffSegment> segments)
    {
        var active = segments
            .Where(s => !s.Moved && SegmentValue(s).Trim().Length > 0)
            .ToList();

        for (var i = 0; i + 1 < active.Count; i++)
        {
            if (active[i].Op == DiffOp.Removed && active[i + 1].Op == DiffOp.Added
                && IsNumberChangePair(SegmentValue(active[i]), SegmentValue(active[i + 1])))
            {
                return true;
            }
        }

        foreach (var group in active.Where(s => s.OldParagraph is not null && s.NewParagraph is not null)
                     .GroupBy(s => (s.OldParagraph, s.NewParagraph)))
        {
            foreach (var rem in group.Where(s => s.Op == DiffOp.Removed))
            {
                foreach (var add in group.Where(s => s.Op == DiffOp.Added))
                {
                    if (IsNumberChangePair(SegmentValue(rem), SegmentValue(add)))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static string SegmentValue(DiffSegment s) => s.Prose.Length > 0 ? s.Prose : s.Text;

    private static bool IsNumberChangePair(string removed, string added)
    {
        var rem = removed.Trim();
        var add = added.Trim();
        if (rem.Length == 0 || add.Length == 0 || string.Equals(rem, add, StringComparison.Ordinal))
        {
            return false;
        }

        if (!rem.Any(char.IsDigit) || !add.Any(char.IsDigit))
        {
            return false;
        }

        if (rem.Length <= ShortNumberSegmentMaxChars && add.Length <= ShortNumberSegmentMaxChars)
        {
            return true;
        }

        return string.Equals(NumberRun().Replace(rem, "0"), NumberRun().Replace(add, "0"), StringComparison.Ordinal);
    }

    private static IReadOnlyList<EditView> BuildEdits(
        IncidentRecord r,
        Dictionary<(long PageId, long RevId), RevisionRow> revisionRows)
    {
        var rawEdits = new List<PendingEdit>();

        if (r.Edits.Count > 0)
        {
            foreach (var e in r.Edits)
            {
                var pageId = e.PageId > 0 ? e.PageId : r.PageId;
                revisionRows.TryGetValue((pageId, e.RevId), out var row);
                var action = MapAction(e.Role);
                rawEdits.Add(BuildPendingEdit(r, pageId, e.RevId, action, row, e));
            }
        }
        else
        {
            var inserting = r.InsertingRevIds.ToHashSet();
            foreach (var revId in r.InsertingRevIds)
            {
                revisionRows.TryGetValue((r.PageId, revId), out var row);
                rawEdits.Add(BuildPendingEdit(r, r.PageId, revId, EditAction.Inserted, row, null));
            }

            foreach (var revId in r.RemovingRevIds.Where(id => !inserting.Contains(id)))
            {
                revisionRows.TryGetValue((r.PageId, revId), out var row);
                rawEdits.Add(BuildPendingEdit(r, r.PageId, revId, EditAction.Removed, row, null));
            }
        }

        var sorted = rawEdits
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.RevId)
            .ToList();

        // When built from bare rev_ids (no explicit IncidentEdit roles), any insertion after a removal is a re-insertion.
        if (r.Edits.Count == 0)
        {
            var seenRemoval = false;
            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Action == EditAction.Removed)
                {
                    seenRemoval = true;
                }
                else if (seenRemoval && sorted[i].Action == EditAction.Inserted)
                {
                    sorted[i] = sorted[i] with { Action = EditAction.Reinserted };
                }
            }
        }

        var result = new List<EditView>(sorted.Count);
        for (var i = 0; i < sorted.Count; i++)
        {
            var cur = sorted[i];
            TimeSpan? live = null;
            if (cur.Action is EditAction.Inserted or EditAction.Reinserted)
            {
                var nextRemoval = FindNextRemoval(sorted, i);
                if (nextRemoval is not null)
                {
                    var delta = nextRemoval.Timestamp - cur.Timestamp;
                    live = delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
                }
            }

            result.Add(new EditView(
                RevId: cur.RevId,
                ParentRevId: cur.ParentRevId,
                PageId: cur.PageId,
                Timestamp: cur.Timestamp,
                Editor: cur.Editor,
                Summary: cur.Summary,
                Action: cur.Action,
                Live: live));
        }

        return result;
    }

    private static PendingEdit? FindNextRemoval(List<PendingEdit> sorted, int index)
    {
        var pageId = sorted[index].PageId;
        for (var j = index + 1; j < sorted.Count; j++)
        {
            if (sorted[j].Action == EditAction.Removed && sorted[j].PageId == pageId)
            {
                return sorted[j];
            }
        }

        for (var j = index + 1; j < sorted.Count; j++)
        {
            if (sorted[j].Action == EditAction.Removed)
            {
                return sorted[j];
            }
        }

        return null;
    }

    private static PendingEdit BuildPendingEdit(
        IncidentRecord r,
        long pageId,
        long revId,
        EditAction action,
        RevisionRow? row,
        IncidentEdit? fallback)
    {
        var parentRevId = NormalizeRevId(row?.ParentRevId) ?? NormalizeRevId(fallback?.ParentRevId);

        DateTimeOffset timestamp;
        if (row is not null)
        {
            timestamp = new DateTimeOffset(DateTime.SpecifyKind(row.Timestamp, DateTimeKind.Utc));
        }
        else if (fallback?.Timestamp is { } ets)
        {
            timestamp = ets.ToUniversalTime();
        }
        else if (action != EditAction.Removed && r.FirstInsertedAt is { } firstIns)
        {
            timestamp = firstIns.ToUniversalTime();
        }
        else if (action == EditAction.Removed && r.LastRemovedAt is { } lastRem)
        {
            timestamp = lastRem.ToUniversalTime();
        }
        else
        {
            timestamp = DateTimeOffset.UnixEpoch;
        }

        var editor = row is not null
            ? EditorDisplay.For(row.User)
            : FallbackEditor(fallback?.UserDisplay, fallback?.IsAnonymous);

        var rawSummary = row is not null ? row.Comment : fallback?.Summary;
        var summary = string.IsNullOrWhiteSpace(rawSummary) ? null : EditorDisplay.MaskComment(rawSummary);

        return new PendingEdit(revId, parentRevId, pageId, timestamp, editor, summary, action);
    }

    internal static EditorLabel FallbackEditor(string? userDisplay, bool? isAnonymous)
    {
        if (string.IsNullOrWhiteSpace(userDisplay))
        {
            return EditorDisplay.For(null);
        }

        var trimmed = userDisplay.Trim();
        if (string.Equals(trimmed, "Hidden editor", StringComparison.OrdinalIgnoreCase))
        {
            return EditorDisplay.For(null);
        }

        if (MaskedIpv4().Match(trimmed) is { Success: true } m4)
        {
            var b0 = m4.Groups[1].Value;
            var b1 = m4.Groups[2].Value;
            return new EditorLabel(EditorKind.IPv4, $"{b0}.{b1}.x.x", "anonymous", null, $"ip4:{b0}.{b1}");
        }

        if (MaskedIpv6().Match(trimmed) is { Success: true } m6)
        {
            var g1 = m6.Groups[1].Value.ToUpperInvariant();
            var g2 = m6.Groups[2].Value.ToUpperInvariant();
            return new EditorLabel(EditorKind.IPv6, $"{g1}:{g2}:\u2026", "anonymous", null, $"ip6:{g1}:{g2}");
        }

        const string TempSuffix = " (temporary account)";
        if (trimmed.EndsWith(TempSuffix, StringComparison.Ordinal))
        {
            return EditorDisplay.For(trimmed[..^TempSuffix.Length].Trim());
        }

        var parsed = EditorDisplay.For(trimmed);
        if (isAnonymous == true && parsed.Kind == EditorKind.Named)
        {
            const string AnonSuffix = " (anonymous)";
            var display = trimmed.EndsWith(AnonSuffix, StringComparison.Ordinal)
                ? trimmed[..^AnonSuffix.Length].Trim()
                : trimmed;
            return new EditorLabel(EditorKind.IPv4, display, "anonymous", null, $"anon:{display}");
        }

        return parsed;
    }

    private static EditAction MapAction(IncidentEditRole role) => role switch
    {
        IncidentEditRole.Inserted => EditAction.Inserted,
        IncidentEditRole.Removed => EditAction.Removed,
        IncidentEditRole.Reinserted => EditAction.Reinserted,
        _ => EditAction.Inserted,
    };

    private static string BuildFallbackTierReason(EvidenceTier tier, IReadOnlyList<SignalView> signals)
    {
        var reasons = signals.Count > 0
            ? string.Join(", ", signals.Select(s => s.Phrase.Chip.ToLowerInvariant()))
            : "taken out by another editor";
        return $"{EvidenceTiers.Get(tier).Label} \u2014 because: {reasons}.";
    }

    private static long? NormalizeRevId(long? revId) => revId is > 0 ? revId : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record PendingEdit(
        long RevId,
        long? ParentRevId,
        long PageId,
        DateTimeOffset Timestamp,
        EditorLabel Editor,
        string? Summary,
        EditAction Action);

    [GeneratedRegex(@"\d+(?:[.,\u00A0 ]\d+)*", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRun();

    [GeneratedRegex(@"^(\d{1,3})\.(\d{1,3})\.x\.x(?:\s*\(anonymous\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex MaskedIpv4();

    [GeneratedRegex(@"^([0-9A-Fa-f]{1,4}):([0-9A-Fa-f]{1,4}):(?:\u2026|\.\.\.)(?:\s*\(anonymous\))?$", RegexOptions.CultureInvariant)]
    private static partial Regex MaskedIpv6();
}
