using System.Globalization;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Web.Features.Network;

public static class NetworkGraphBuilder
{
    public const int MaxAccounts = 40;
    public const int MaxArticles = 25;

    public static NetworkGraph Build(IReadOnlyList<IncidentView> incidents, NetworkFilter f)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(f);

        if (incidents.Count == 0)
        {
            return new NetworkGraph([], [], []);
        }

        var (fromYear, toYear) = f.FromYear is { } fy && f.ToYear is { } ty && fy > ty
            ? (ty, fy)
            : (f.FromYear, f.ToYear);

        var articleMeta = new Dictionary<long, (string TitleFi, string? TitleEn)>();
        foreach (var incident in incidents)
        {
            foreach (var art in incident.Articles)
            {
                MergeArticleMeta(articleMeta, art.PageId, art.TitleFi, art.TitleEn);
            }
        }

        var accountLabels = new Dictionary<string, EditorLabel>(StringComparer.Ordinal);
        var cellAccumulators = new Dictionary<(string AccountKey, long PageId), CellAccumulator>();

        foreach (var incident in incidents)
        {
            if (f.MinTier is { } minTier && incident.Tier < minTier)
            {
                continue;
            }

            var fallbackPageId = incident.Articles.Count > 0
                ? incident.Articles[0].PageId
                : incident.Source?.PageId ?? 0L;

            var clusterId = !string.IsNullOrWhiteSpace(incident.ClusterId)
                ? incident.ClusterId.Trim()
                : !string.IsNullOrWhiteSpace(incident.Source?.ClusterId)
                    ? incident.Source.ClusterId.Trim()
                    : null;

            foreach (var edit in incident.Edits)
            {
                if (!f.IncludeRemovers && edit.Action == EditAction.Removed)
                {
                    continue;
                }

                if (fromYear is not null || toYear is not null)
                {
                    var editYear = ResolveEditYear(edit, incident);
                    if (fromYear is { } minY && editYear < minY) continue;
                    if (toYear is { } maxY && editYear > maxY) continue;
                }

                var pageId = edit.PageId > 0 ? edit.PageId : fallbackPageId;
                var label = edit.Editor;
                var accountKey = label.GroupingKey;

                accountLabels.TryAdd(accountKey, label);
                if (!articleMeta.ContainsKey(pageId))
                {
                    articleMeta[pageId] = ($"Page {pageId.ToString(CultureInfo.InvariantCulture)}", null);
                }

                var cellKey = (accountKey, pageId);
                if (!cellAccumulators.TryGetValue(cellKey, out var acc))
                {
                    acc = new CellAccumulator(accountKey, pageId);
                    cellAccumulators[cellKey] = acc;
                }

                acc.Add(incident.Id, edit.RevId, edit.Action, clusterId);
            }
        }

        if (cellAccumulators.Count == 0)
        {
            return new NetworkGraph([], [], []);
        }

        var accountStats = new Dictionary<string, (int ArticlesTouched, int TotalEdits)>(StringComparer.Ordinal);
        var articleStats = new Dictionary<long, (int AccountsTouching, int TotalEdits)>();

        foreach (var acc in cellAccumulators.Values)
        {
            var editsInCell = acc.TotalEdits;

            if (accountStats.TryGetValue(acc.AccountKey, out var aStat))
            {
                accountStats[acc.AccountKey] = (aStat.ArticlesTouched + 1, aStat.TotalEdits + editsInCell);
            }
            else
            {
                accountStats[acc.AccountKey] = (1, editsInCell);
            }

            if (articleStats.TryGetValue(acc.PageId, out var pStat))
            {
                articleStats[acc.PageId] = (pStat.AccountsTouching + 1, pStat.TotalEdits + editsInCell);
            }
            else
            {
                articleStats[acc.PageId] = (1, editsInCell);
            }
        }

        var orderedAccounts = accountStats
            .OrderByDescending(static kv => kv.Value.ArticlesTouched)
            .ThenByDescending(static kv => kv.Value.TotalEdits)
            .ThenBy(static kv => kv.Key, StringComparer.Ordinal)
            .Select(kv =>
            {
                var label = accountLabels[kv.Key];
                return new AccountNode(kv.Key, label, label.IsAnonymous);
            })
            .ToList();

        var orderedArticles = articleStats
            .OrderByDescending(static kv => kv.Value.AccountsTouching)
            .ThenByDescending(static kv => kv.Value.TotalEdits)
            .ThenBy(static kv => kv.Key)
            .Select(kv =>
            {
                var (titleFi, titleEn) = articleMeta[kv.Key];
                return new ArticleNode(kv.Key, titleFi, titleEn);
            })
            .ToList();

        var keptAccounts = orderedAccounts.Count > MaxAccounts
            ? orderedAccounts.Take(MaxAccounts).ToList()
            : orderedAccounts;
        var keptArticles = orderedArticles.Count > MaxArticles
            ? orderedArticles.Take(MaxArticles).ToList()
            : orderedArticles;

        var omittedAccounts = Math.Max(0, orderedAccounts.Count - keptAccounts.Count);
        var omittedArticles = Math.Max(0, orderedArticles.Count - keptArticles.Count);

        var cells = new List<MatrixCell>();
        foreach (var account in keptAccounts)
        {
            foreach (var article in keptArticles)
            {
                if (cellAccumulators.TryGetValue((account.Key, article.PageId), out var acc))
                {
                    cells.Add(acc.ToCell());
                }
            }
        }

        return new NetworkGraph(keptAccounts, keptArticles, cells)
        {
            OmittedAccounts = omittedAccounts,
            OmittedArticles = omittedArticles,
        };
    }

    private static int ResolveEditYear(EditView edit, IncidentView incident)
    {
        if (edit.Timestamp == DateTimeOffset.UnixEpoch && incident.FirstSeen != DateTimeOffset.UnixEpoch)
        {
            return incident.FirstSeen.UtcDateTime.Year;
        }

        return edit.Timestamp.UtcDateTime.Year;
    }

    private static void MergeArticleMeta(
        Dictionary<long, (string TitleFi, string? TitleEn)> map,
        long pageId,
        string titleFi,
        string? titleEn)
    {
        var fallbackTitle = $"Page {pageId.ToString(CultureInfo.InvariantCulture)}";
        var normalizedEn = string.IsNullOrWhiteSpace(titleEn) ? null : titleEn.Trim();

        if (!map.TryGetValue(pageId, out var existing))
        {
            map[pageId] = (string.IsNullOrWhiteSpace(titleFi) ? fallbackTitle : titleFi, normalizedEn);
            return;
        }

        var chosenFi = string.Equals(existing.TitleFi, fallbackTitle, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(titleFi)
            ? titleFi
            : existing.TitleFi;
        var chosenEn = existing.TitleEn ?? normalizedEn;
        map[pageId] = (chosenFi, chosenEn);
    }

    private sealed class CellAccumulator(string accountKey, long pageId)
    {
        private readonly List<(string IncidentId, long RevId)> _edits = [];
        private readonly HashSet<(string IncidentId, long RevId)> _seenEdits = [];
        private readonly List<string> _clusterIds = [];
        private readonly HashSet<string> _seenClusters = new(StringComparer.Ordinal);

        public string AccountKey { get; } = accountKey;
        public long PageId { get; } = pageId;
        public int Inserted { get; private set; }
        public int Removed { get; private set; }
        public int Reinserted { get; private set; }
        public int TotalEdits => Inserted + Removed + Reinserted;

        public void Add(string incidentId, long revId, EditAction action, string? clusterId)
        {
            switch (action)
            {
                case EditAction.Inserted:
                    Inserted++;
                    break;
                case EditAction.Removed:
                    Removed++;
                    break;
                case EditAction.Reinserted:
                    Reinserted++;
                    break;
            }

            var pair = (incidentId, revId);
            if (_seenEdits.Add(pair))
            {
                _edits.Add(pair);
            }

            if (!string.IsNullOrWhiteSpace(clusterId) && _seenClusters.Add(clusterId))
            {
                _clusterIds.Add(clusterId);
            }
        }

        public MatrixCell ToCell() =>
            new(AccountKey, PageId, Inserted, Removed, Reinserted, _edits, _clusterIds);
    }
}
