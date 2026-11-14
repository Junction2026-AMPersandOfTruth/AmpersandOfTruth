using System.Globalization;

namespace AmpersandOfTruth.Web.Features.Hotspots;

public static class HotspotAnalytics
{
    public const int StartYear = 2003;
    public const int EndYear = 2026;

    public static IReadOnlyList<int> ParseYearlyReverts(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        var parts = csv.Split(',', StringSplitOptions.TrimEntries);
        var values = new List<int>(parts.Length);
        foreach (var part in parts)
        {
            if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                values.Add(Math.Max(0, n));
            }
        }

        return values;
    }

    public static bool TryParseMonth(string? raw, out int year, out int month)
    {
        year = 0;
        month = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var trimmed = raw.AsSpan().Trim();
        if (trimmed.Length != 7 || trimmed[4] != '-')
        {
            return false;
        }

        if (!int.TryParse(trimmed[..4], NumberStyles.None, CultureInfo.InvariantCulture, out year) ||
            !int.TryParse(trimmed[5..], NumberStyles.None, CultureInfo.InvariantCulture, out month))
        {
            year = 0;
            month = 0;
            return false;
        }

        if (year is < 1900 or > 2100 || month is < 1 or > 12)
        {
            year = 0;
            month = 0;
            return false;
        }

        return true;
    }

    public static IReadOnlyList<HotYear> AggregateByYear(IReadOnlyList<HotMonth> months)
    {
        ArgumentNullException.ThrowIfNull(months);
        if (months.Count == 0)
        {
            return [];
        }

        var byYear = new SortedDictionary<int, List<(int MonthNum, HotMonth Row)>>();
        foreach (var row in months)
        {
            if (!TryParseMonth(row.Month, out var yr, out var mo))
            {
                continue;
            }

            if (!byYear.TryGetValue(yr, out var bucket))
            {
                bucket = [];
                byYear[yr] = bucket;
            }

            bucket.Add((mo, row));
        }

        var result = new List<HotYear>(byYear.Count);
        foreach (var (yr, entries) in byYear)
        {
            var totalReverts = 0;
            var totalEpisodes = 0;
            var totalEdits = 0;

            foreach (var (_, row) in entries)
            {
                totalReverts += Math.Max(0, row.Reverts);
                totalEpisodes += Math.Max(0, row.Episodes);
                totalEdits += Math.Max(0, row.Edits);
            }

            var top = PickYearTopArticle(entries);
            result.Add(new HotYear(
                Year: yr,
                Reverts: totalReverts,
                Episodes: totalEpisodes,
                Edits: totalEdits,
                TopPageId: top.PageId,
                TopTitleFi: top.TitleFi,
                TopTitleEn: top.TitleEn));
        }

        return result;
    }

    public static IReadOnlyList<HotSpike> PickTopSpikes(IReadOnlyList<HotYear> years, int count = 5)
    {
        ArgumentNullException.ThrowIfNull(years);
        if (years.Count == 0 || count <= 0)
        {
            return [];
        }

        return years
            .Where(static y =>
                (y.Reverts > 0 || y.Episodes > 0 || y.Edits > 0) &&
                !string.IsNullOrWhiteSpace(y.TopDisplayTitle))
            .OrderByDescending(static y => y.Reverts)
            .ThenByDescending(static y => y.Episodes)
            .ThenByDescending(static y => y.Edits)
            .ThenBy(static y => y.Year)
            .Take(count)
            .OrderBy(static y => y.Year)
            .Select(static y => new HotSpike(
                Year: y.Year,
                Reverts: y.Reverts,
                Episodes: y.Episodes,
                Edits: y.Edits,
                PageId: y.TopPageId,
                TitleFi: !string.IsNullOrWhiteSpace(y.TopTitleFi) ? y.TopTitleFi : y.TopTitleEn!,
                TitleEn: y.TopTitleEn))
            .ToList();
    }

    public static int YearBarValue(HotYear year, int maxReverts, int maxEpisodes) =>
        maxReverts > 0
            ? year.Reverts
            : (maxEpisodes > 0 ? year.Episodes : year.Edits);

    public static string ActivitySummary(IReadOnlyList<HotYear> years, IReadOnlyList<HotSpike> spikes)
    {
        ArgumentNullException.ThrowIfNull(years);
        ArgumentNullException.ThrowIfNull(spikes);

        if (years.Count == 0)
        {
            return "No yearly activity available.";
        }

        var peak = years
            .OrderByDescending(static y => y.Reverts)
            .ThenByDescending(static y => y.Episodes)
            .ThenByDescending(static y => y.Edits)
            .ThenBy(static y => y.Year)
            .First();

        if (peak.Reverts == 0 && peak.Episodes == 0 && peak.Edits == 0)
        {
            return "No disputed edits recorded across the selected years.";
        }

        var removals = peak.Reverts.ToString("N0", CultureInfo.InvariantCulture);
        var episodes = peak.Episodes.ToString("N0", CultureInfo.InvariantCulture);
        var epNoun = peak.Episodes == 1 ? "conflict episode" : "conflict episodes";
        var remNoun = peak.Reverts == 1 ? "removed edit" : "removed edits";

        if (!string.IsNullOrWhiteSpace(peak.TopDisplayTitle))
        {
            return $"Peak disputed activity in {peak.Year.ToString(CultureInfo.InvariantCulture)} ({removals} {remNoun}, {episodes} {epNoun}), led by {peak.TopDisplayTitle}.";
        }

        return $"Peak disputed activity in {peak.Year.ToString(CultureInfo.InvariantCulture)} ({removals} {remNoun}, {episodes} {epNoun}).";
    }

    private static (long? PageId, string? TitleFi, string? TitleEn) PickYearTopArticle(
        List<(int MonthNum, HotMonth Row)> entries)
    {
        var candidates = entries
            .Where(static e => !string.IsNullOrWhiteSpace(e.Row.TopTitleFi) || !string.IsNullOrWhiteSpace(e.Row.TopTitleEn))
            .GroupBy(static e => e.Row.TopPageId is > 0
                ? $"id:{e.Row.TopPageId.Value.ToString(CultureInfo.InvariantCulture)}"
                : $"title:{(e.Row.TopTitleFi ?? e.Row.TopTitleEn ?? string.Empty).Trim().ToUpperInvariant()}")
            .Select(static g =>
            {
                var first = g.OrderBy(static x => x.MonthNum).First();
                var titleFi = g.Select(static x => x.Row.TopTitleFi).FirstOrDefault(static t => !string.IsNullOrWhiteSpace(t));
                var titleEn = g.Select(static x => x.Row.TopTitleEn).FirstOrDefault(static t => !string.IsNullOrWhiteSpace(t));
                return new
                {
                    PageId = first.Row.TopPageId,
                    TitleFi = titleFi,
                    TitleEn = titleEn,
                    SumReverts = g.Sum(static x => Math.Max(0, x.Row.Reverts)),
                    SumEpisodes = g.Sum(static x => Math.Max(0, x.Row.Episodes)),
                    SumEdits = g.Sum(static x => Math.Max(0, x.Row.Edits)),
                    FirstMonth = first.MonthNum,
                };
            })
            .OrderByDescending(static c => c.SumReverts)
            .ThenByDescending(static c => c.SumEpisodes)
            .ThenByDescending(static c => c.SumEdits)
            .ThenBy(static c => c.FirstMonth)
            .FirstOrDefault();

        return candidates is null
            ? (null, null, null)
            : (candidates.PageId, candidates.TitleFi, candidates.TitleEn);
    }
}
