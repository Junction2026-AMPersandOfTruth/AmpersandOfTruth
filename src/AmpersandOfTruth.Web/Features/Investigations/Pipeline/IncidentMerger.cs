using System.Text;
using System.Text.RegularExpressions;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// One incident per inserted content, not per revert (the frozen contract's claim-side counting, CONTRACT.md §3; P1.S3
/// approach A, <c>research/30-s3-extract/variant.py merge_page_items</c>). Stage 4 assembles one incident per identity
/// revert, and a re-insertion chain (insert → revert → re-insert → revert …) yields one per revert, each listing the
/// re-insertions in its <c>inserting_rev_ids</c>. This groups incidents on the same page whose <c>inserting_rev_ids</c>
/// overlap, or whose normalised inserted prose is identical (<see cref="ProseKey"/>), transitively — never across pages,
/// and never when the merged group would have no inserting revision that isn't also a removing one (S3-A's guard).
/// <para>The merged incident takes everything that judges the content (tier, score, reason, signals, assessment, claim)
/// from its strongest member (tier, then score, then id), and keeps that member's id, so links stay stable;
/// <c>cluster_id</c> = that id. Revisions and edits are the union, timeline fields span the group. A group of one is
/// returned as the same instance.</para>
/// Pure and thread-safe.
/// </summary>
public static partial class IncidentMerger
{
    /// <summary>One assembled incident and its <see cref="ProseKey"/> (null or empty = merge on revisions only).</summary>
    public sealed record Item(IncidentRecord Incident, string? ProseKey = null);

    /// <summary>The merged incidents, in the order of each group's first member in <paramref name="items"/>.</summary>
    public static List<IncidentRecord> Merge(IReadOnlyList<Item> items)
    {
        var groups = items.Select((it, index) => (it, index))
            .GroupBy(x => x.it.Incident.PageId)
            .SelectMany(page => GroupPage([.. page]));
        return [.. groups
            .OrderBy(g => g.Min(x => x.index))
            .Select(g => g.Count == 1 ? g[0].it.Incident : Combine([.. g.Select(x => x.it.Incident)]))];
    }

    /// <summary>Connected components of one page's items (union–find, S3-A order: by first removing revision).</summary>
    private static List<List<(Item it, int index)>> GroupPage(List<(Item it, int index)> page)
    {
        var ordered = page.OrderBy(x => x.it.Incident.RemovingRevIds.DefaultIfEmpty(long.MaxValue).Min()).ThenBy(x => x.index).ToList();
        var n = ordered.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        var ins = ordered.Select(x => x.it.Incident.InsertingRevIds.ToHashSet()).ToArray();
        var rem = ordered.Select(x => x.it.Incident.RemovingRevIds.ToHashSet()).ToArray();

        int Find(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }

        void TryUnion(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra == rb) return;
            var mergedIns = new HashSet<long>(ins[ra]);
            mergedIns.UnionWith(ins[rb]);
            var mergedRem = new HashSet<long>(rem[ra]);
            mergedRem.UnionWith(rem[rb]);
            // S3-A's guard: never merge if every inserting revision would also be a removing one.
            if (mergedIns.All(mergedRem.Contains)) return;
            if (ra > rb) (ra, rb) = (rb, ra);
            parent[rb] = ra;
            ins[ra] = mergedIns;
            rem[ra] = mergedRem;
        }

        var byProse = new Dictionary<string, int>(StringComparer.Ordinal);
        var byRev = new Dictionary<long, int>();
        for (var i = 0; i < n; i++)
        {
            if (ordered[i].it.ProseKey is { Length: > 0 } key)
            {
                if (byProse.TryGetValue(key, out var prev)) TryUnion(prev, i);
                else byProse[key] = i;
            }
            foreach (var rev in ordered[i].it.Incident.InsertingRevIds)
            {
                if (byRev.TryGetValue(rev, out var prev)) TryUnion(prev, i);
                else byRev[rev] = i;
            }
        }

        return [.. Enumerable.Range(0, n).GroupBy(Find).Select(g => g.Select(i => ordered[i]).ToList())];
    }

    private static IncidentRecord Combine(List<IncidentRecord> members)
    {
        var lead = members
            .OrderByDescending(m => m.Tier).ThenByDescending(m => m.Score).ThenBy(m => m.Id, StringComparer.Ordinal).First();

        // Edits: one per revision. A revision can be "inserted" in one member and "reinserted" in another (the head of a
        // later revert's span is an earlier revert's re-insertion); the stronger role wins: removed > reinserted > inserted.
        var edits = members.SelectMany(m => m.Edits)
            .GroupBy(e => e.RevId)
            .Select(g => g.OrderByDescending(e => RoleRank(e.Role)).First())
            .OrderBy(e => e.Timestamp).ThenBy(e => e.RevId)
            .ToList();
        var when = edits.Where(e => e.Timestamp is not null).ToDictionary(e => e.RevId, e => e.Timestamp!.Value);

        var removing = members.SelectMany(m => m.RemovingRevIds).Distinct().ToList();
        var removingSet = removing.ToHashSet();
        var inserting = members.SelectMany(m => m.InsertingRevIds).Distinct().ToList();
        var clean = inserting.Where(r => !removingSet.Contains(r)).ToList();
        if (clean.Count > 0) inserting = clean;

        List<long> ByTime(IEnumerable<long> revs) =>
            [.. revs.OrderBy(r => when.TryGetValue(r, out var t) ? t : DateTimeOffset.MaxValue).ThenBy(r => r)];

        return lead with
        {
            InsertingRevIds = ByTime(inserting),
            RemovingRevIds = ByTime(removing),
            Edits = edits,
            FirstInsertedAt = Min(members.Select(m => m.FirstInsertedAt)),
            LastRemovedAt = Max(members.Select(m => m.LastRemovedAt)),
            TotalExposureSeconds = Max(members.Select(m => m.TotalExposureSeconds)),
            ReinsertionCount = Max(members.Select(m => m.ReinsertionCount)),
            ClusterId = lead.Id,
        };
    }

    private static int RoleRank(IncidentEditRole role) => role switch
    {
        IncidentEditRole.Removed => 2,
        IncidentEditRole.Reinserted => 1,
        _ => 0,
    };

    private static T? Min<T>(IEnumerable<T?> values) where T : struct, IComparable<T> => Best(values, -1);
    private static T? Max<T>(IEnumerable<T?> values) where T : struct, IComparable<T> => Best(values, +1);

    private static T? Best<T>(IEnumerable<T?> values, int sign) where T : struct, IComparable<T>
    {
        T? best = null;
        foreach (var v in values)
            if (v is { } x && (best is null || sign * x.CompareTo(best.Value) > 0)) best = x;
        return best;
    }

    /// <summary>
    /// S3-A's <c>normalise_prose</c>: NFC, case folded, " … " fragment joins and whitespace collapsed, punctuation
    /// trimmed at both ends. Empty when nothing alphanumeric is left — empty keys never merge.
    /// </summary>
    public static string ProseKey(string? prose)
    {
        if (string.IsNullOrEmpty(prose)) return "";
        var s = prose.Normalize(NormalizationForm.FormC).ToUpperInvariant().ToLowerInvariant();
        s = FragmentJoin().Replace(s, " ");
        s = Collapse(Collapse(s).Trim(StripChars));
        return s.Any(char.IsLetterOrDigit) ? s : "";
    }

    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static readonly char[] StripChars = ".,;:!?'“”«»()[]{}-–— \t\n\r\"".ToCharArray();

    [GeneratedRegex(@"\s+…\s+")]
    private static partial Regex FragmentJoin();
}
