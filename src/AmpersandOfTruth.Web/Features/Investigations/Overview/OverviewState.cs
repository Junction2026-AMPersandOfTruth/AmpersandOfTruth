using System.Collections.Frozen;
using System.Globalization;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.View;

namespace AmpersandOfTruth.Web.Features.Investigations.Overview;

public enum IncidentSort { Relevance, Recent, LongestLive }

public sealed record OverviewFilter(
    IncidentSort Sort,
    IReadOnlySet<EvidenceTier> Tiers,
    IReadOnlySet<string> Signals,
    long? ArticleId,
    int? FromYear,
    int? ToYear)
{
    public static OverviewFilter Default { get; } = new(
        IncidentSort.Relevance,
        FrozenSet<EvidenceTier>.Empty,
        FrozenSet<string>.Empty,
        null,
        null,
        null);

    public bool IsDefault =>
        Sort == IncidentSort.Relevance
        && Tiers.Count == 0
        && Signals.Count == 0
        && ArticleId is null
        && FromYear is null
        && ToYear is null;

    public static OverviewFilter Parse(string? sort, string? tier, string? signal, long? article, int? from, int? to)
    {
        var parsedSort = ParseSort(sort);
        var parsedTiers = ParseTiers(tier);
        var parsedSignals = ParseSignals(signal);
        var parsedArticle = article is > 0 ? article : null;
        var (parsedFrom, parsedTo) = NormalizeYears(from, to);

        return new OverviewFilter(parsedSort, parsedTiers, parsedSignals, parsedArticle, parsedFrom, parsedTo);
    }

    public IReadOnlyDictionary<string, object?> ToQuery()
    {
        var tiers = FormatTiers(Tiers);
        var signals = FormatSignals(Signals);
        var (from, to) = NormalizeYears(FromYear, ToYear);

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sort"] = FormatSort(Sort),
            ["tier"] = tiers,
            ["signal"] = signals,
            ["article"] = ArticleId is > 0 ? ArticleId : null,
            ["from"] = from,
            ["to"] = to,
        };
    }

    public string ToQueryString()
    {
        var query = ToQuery();
        var parts = new List<string>(6);
        foreach (var (key, value) in query)
        {
            if (value is null) continue;
            var formatted = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(formatted))
            {
                parts.Add($"{key}={formatted}");
            }
        }

        return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
    }

    public OverviewFilter ToggleTier(EvidenceTier t)
    {
        if (!Enum.IsDefined(t)) return this;
        var next = new HashSet<EvidenceTier>(Tiers);
        if (!next.Remove(t)) next.Add(t);
        return this with { Tiers = next.ToFrozenSet() };
    }

    public OverviewFilter ToggleSignal(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return this;
        var canonical = SignalCatalog.Normalize(id);
        var next = Signals.Select(SignalCatalog.Normalize).ToHashSet(StringComparer.Ordinal);
        if (!next.Remove(canonical)) next.Add(canonical);
        return this with { Signals = next.ToFrozenSet(StringComparer.Ordinal) };
    }

    public OverviewFilter WithArticle(long? id) =>
        this with { ArticleId = id is > 0 ? id : null };

    public OverviewFilter WithYears(int? from, int? to)
    {
        var (normalizedFrom, normalizedTo) = NormalizeYears(from, to);
        return this with { FromYear = normalizedFrom, ToYear = normalizedTo };
    }

    public OverviewFilter WithSort(IncidentSort s) =>
        this with { Sort = Enum.IsDefined(s) ? s : IncidentSort.Relevance };

    public bool Equals(OverviewFilter? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        return Sort == other.Sort
            && ArticleId == other.ArticleId
            && FromYear == other.FromYear
            && ToYear == other.ToYear
            && Tiers.SetEquals(other.Tiers)
            && NormalizedSignals(Signals).SetEquals(NormalizedSignals(other.Signals));
    }

    public override int GetHashCode()
    {
        var tierHash = 0;
        foreach (var t in Tiers)
        {
            tierHash ^= t.GetHashCode();
        }

        var signalHash = 0;
        foreach (var s in NormalizedSignals(Signals))
        {
            signalHash ^= StringComparer.Ordinal.GetHashCode(s);
        }

        return HashCode.Combine(Sort, tierHash, signalHash, ArticleId, FromYear, ToYear);
    }

    private static HashSet<string> NormalizedSignals(IReadOnlySet<string> signals) =>
        signals.Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(SignalCatalog.Normalize)
            .ToHashSet(StringComparer.Ordinal);

    private static IncidentSort ParseSort(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort)) return IncidentSort.Relevance;
        return sort.Trim().ToLowerInvariant() switch
        {
            "recent" => IncidentSort.Recent,
            "longest-live" or "longest_live" or "longestlive" or "live" or "exposure" => IncidentSort.LongestLive,
            _ => IncidentSort.Relevance,
        };
    }

    private static string? FormatSort(IncidentSort sort) => sort switch
    {
        IncidentSort.Recent => "recent",
        IncidentSort.LongestLive => "longest-live",
        _ => null,
    };

    private static FrozenSet<EvidenceTier> ParseTiers(string? tier)
    {
        if (string.IsNullOrWhiteSpace(tier)) return FrozenSet<EvidenceTier>.Empty;

        var set = new HashSet<EvidenceTier>();
        foreach (var part in tier.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
                && level is >= 1 and <= 3)
            {
                set.Add((EvidenceTier)level);
            }
            else if (EvidenceTiers.TryParseId(part.ToLowerInvariant(), out var parsedTier))
            {
                set.Add(parsedTier);
            }
        }

        return set.ToFrozenSet();
    }

    private static string? FormatTiers(IReadOnlySet<EvidenceTier> tiers)
    {
        if (tiers.Count == 0) return null;
        var levels = tiers
            .Where(Enum.IsDefined)
            .Select(static t => ((int)t).ToString(CultureInfo.InvariantCulture))
            .Order(StringComparer.Ordinal)
            .ToList();
        return levels.Count == 0 ? null : string.Join(",", levels);
    }

    private static FrozenSet<string> ParseSignals(string? signal)
    {
        if (string.IsNullOrWhiteSpace(signal)) return FrozenSet<string>.Empty;

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in signal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = SignalCatalog.Normalize(part);
            if (IsValidSignalToken(normalized))
            {
                set.Add(normalized);
            }
        }

        return set.ToFrozenSet(StringComparer.Ordinal);
    }

    private static string? FormatSignals(IReadOnlySet<string> signals)
    {
        if (signals.Count == 0) return null;
        var canonical = signals
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Select(SignalCatalog.Normalize)
            .Where(IsValidSignalToken)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        return canonical.Count == 0 ? null : string.Join(",", canonical);
    }

    private static bool IsValidSignalToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        foreach (var ch in token)
        {
            var ok = ch is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_';
            if (!ok) return false;
        }
        return true;
    }

    private static (int? From, int? To) NormalizeYears(int? from, int? to)
    {
        var validFrom = from is >= 1 and <= 9999 ? from : null;
        var validTo = to is >= 1 and <= 9999 ? to : null;
        if (validFrom is { } f && validTo is { } t && f > t)
        {
            return (t, f);
        }
        return (validFrom, validTo);
    }
}

public static class OverviewState
{
    public static IReadOnlyList<IncidentView> Apply(IEnumerable<IncidentView> incidents, OverviewFilter f)
    {
        ArgumentNullException.ThrowIfNull(incidents);
        ArgumentNullException.ThrowIfNull(f);

        HashSet<string>? selectedSignals = f.Signals.Count > 0
            ? f.Signals.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(SignalCatalog.Normalize).ToHashSet(StringComparer.Ordinal)
            : null;

        var (fromYear, toYear) = f.FromYear is { } fy && f.ToYear is { } ty && fy > ty
            ? (ty, fy)
            : (f.FromYear, f.ToYear);

        var filtered = incidents.Where(i =>
        {
            if (f.Tiers.Count > 0 && !f.Tiers.Contains(i.Tier))
            {
                return false;
            }

            if (selectedSignals is { Count: > 0 }
                && !i.Signals.Any(s => selectedSignals.Contains(SignalCatalog.Normalize(s.Id))))
            {
                return false;
            }

            if (f.ArticleId is { } articleId && !i.Articles.Any(a => a.PageId == articleId))
            {
                return false;
            }

            if (fromYear is not null || toYear is not null)
            {
                var startYear = i.FirstSeen.Year;
                var endYear = Math.Max(startYear, (i.LastRemoved ?? i.FirstSeen).Year);
                if (fromYear is { } minYear && endYear < minYear) return false;
                if (toYear is { } maxYear && startYear > maxYear) return false;
            }

            return true;
        });

        var sorted = f.Sort switch
        {
            IncidentSort.Recent => filtered
                .OrderByDescending(static i => i.FirstSeen)
                .ThenBy(static i => i.Rank)
                .ThenBy(static i => i.Id, StringComparer.Ordinal),
            IncidentSort.LongestLive => filtered
                .OrderByDescending(static i => i.TotalLive)
                .ThenBy(static i => i.Rank)
                .ThenBy(static i => i.Id, StringComparer.Ordinal),
            _ => filtered
                .OrderBy(static i => i.Rank)
                .ThenBy(static i => i.Id, StringComparer.Ordinal),
        };

        return sorted.ToList();
    }

    public static IReadOnlyDictionary<EvidenceTier, int> TierCounts(IEnumerable<IncidentView> incidents)
    {
        ArgumentNullException.ThrowIfNull(incidents);

        var counts = new Dictionary<EvidenceTier, int>
        {
            [EvidenceTier.Reverted] = 0,
            [EvidenceTier.Contested] = 0,
            [EvidenceTier.Campaign] = 0,
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

    public static IReadOnlyList<(SignalPhrase Phrase, int Count)> SignalCounts(IEnumerable<IncidentView> incidents)
    {
        ArgumentNullException.ThrowIfNull(incidents);

        var tallies = new Dictionary<string, (SignalPhrase Phrase, int Count)>(StringComparer.Ordinal);

        foreach (var incident in incidents)
        {
            var seenInIncident = new HashSet<string>(StringComparer.Ordinal);
            foreach (var signal in incident.Signals)
            {
                if (string.IsNullOrWhiteSpace(signal.Id)) continue;
                var canonical = SignalCatalog.Normalize(signal.Id);
                if (!seenInIncident.Add(canonical)) continue;

                if (tallies.TryGetValue(canonical, out var existing))
                {
                    tallies[canonical] = (existing.Phrase, existing.Count + 1);
                }
                else
                {
                    var phrase = signal.Phrase.Id == canonical
                        ? signal.Phrase
                        : SignalCatalog.For(canonical, signal.Phrase.IsKnown ? null : signal.Phrase.Chip);
                    tallies[canonical] = (phrase, 1);
                }
            }
        }

        return tallies.Values
            .OrderByDescending(static x => x.Count)
            .ThenBy(static x => x.Phrase.Chip, StringComparer.Ordinal)
            .ThenBy(static x => x.Phrase.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static string WhatHappened(IncidentView i)
    {
        ArgumentNullException.ThrowIfNull(i);

        var inserts = Math.Max(1, i.InsertCount);
        var accounts = Math.Max(1, i.AccountCount);

        var insertPart = inserts == 1
            ? "Inserted once"
            : string.Create(CultureInfo.InvariantCulture, $"Inserted {inserts} times");

        var accountPart = accounts == 1
            ? "by 1 account"
            : string.Create(CultureInfo.InvariantCulture, $"by {accounts} accounts");

        var spanPart = "";
        if (inserts > 1)
        {
            var span = InsertSpan(i);
            if (span >= TimeSpan.FromHours(1))
            {
                spanPart = $" over {Duration(span)}";
            }
            else if (span > TimeSpan.Zero)
            {
                spanPart = $" in {Duration(span)}";
            }
        }

        var livePart = inserts == 1
            ? $"live for {Duration(i.TotalLive)}."
            : $"live for {Duration(i.TotalLive)} in total.";

        return $"{insertPart} {accountPart}{spanPart}; {livePart}";
    }

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.FromHours(1))
        {
            return "under an hour";
        }

        if (t < TimeSpan.FromDays(1))
        {
            var hours = (int)t.TotalHours;
            return hours == 1
                ? "1 hour"
                : string.Create(CultureInfo.InvariantCulture, $"{hours} hours");
        }

        var totalDays = (int)t.TotalDays;
        if (totalDays < 365)
        {
            return totalDays == 1
                ? "1 day"
                : string.Create(CultureInfo.InvariantCulture, $"{totalDays} days");
        }

        var years = totalDays / 365;
        var months = Math.Min(11, (totalDays % 365) / 30);

        var yearText = years == 1
            ? "1 year"
            : string.Create(CultureInfo.InvariantCulture, $"{years} years");

        if (months == 0)
        {
            return yearText;
        }

        var monthText = months == 1
            ? "1 month"
            : string.Create(CultureInfo.InvariantCulture, $"{months} months");

        return $"{yearText} {monthText}";
    }

    private static TimeSpan InsertSpan(IncidentView incident)
    {
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;
        var count = 0;

        foreach (var edit in incident.Edits)
        {
            if (edit.Action is not (EditAction.Inserted or EditAction.Reinserted)) continue;
            count++;
            if (first is null || edit.Timestamp < first.Value) first = edit.Timestamp;
            if (last is null || edit.Timestamp > last.Value) last = edit.Timestamp;
        }

        return count >= 2 && first is { } f && last is { } l && l > f
            ? l - f
            : TimeSpan.Zero;
    }
}
