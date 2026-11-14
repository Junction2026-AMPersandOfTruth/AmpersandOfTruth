using System.Globalization;
using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Web.Features.Network;

public enum EdgeRole { Inserted, Removed, Reinserted }

public sealed record AccountNode(string Key, EditorLabel Label, bool Anonymous);

public sealed record ArticleNode(long PageId, string TitleFi, string? TitleEn)
{
    public bool HasDistinctEnglishTitle =>
        !string.IsNullOrWhiteSpace(TitleEn) &&
        !string.Equals(TitleEn, TitleFi, StringComparison.OrdinalIgnoreCase);
}

public sealed record MatrixCell(
    string AccountKey,
    long PageId,
    int Inserted,
    int Removed,
    int Reinserted,
    IReadOnlyList<(string IncidentId, long RevId)> Edits,
    IReadOnlyList<string> ClusterIds)
{
    public int TotalEdits => Inserted + Removed + Reinserted;

    public EdgeRole PrimaryRole =>
        Reinserted > 0 ? EdgeRole.Reinserted
        : Inserted > 0 ? EdgeRole.Inserted
        : EdgeRole.Removed;
}

public sealed record NetworkGraph(
    IReadOnlyList<AccountNode> Accounts,
    IReadOnlyList<ArticleNode> Articles,
    IReadOnlyList<MatrixCell> Cells)
{
    public bool TooSmall => Accounts.Count < 4;

    public int OmittedAccounts { get; init; }

    public int OmittedArticles { get; init; }

    public bool HasAnonymousAccounts => Accounts.Any(static a => a.Anonymous);
}

public sealed record NetworkFilter(
    EvidenceTier? MinTier,
    int? FromYear,
    int? ToYear,
    bool IncludeRemovers)
{
    public static NetworkFilter Default { get; } = new(null, null, null, IncludeRemovers: true);

    public bool IsDefault =>
        MinTier is null &&
        FromYear is null &&
        ToYear is null &&
        IncludeRemovers;

    public NetworkFilter WithMinTier(EvidenceTier? minTier) =>
        this with { MinTier = minTier };

    public NetworkFilter WithYears(int? fromYear, int? toYear) =>
        this with { FromYear = fromYear, ToYear = toYear };

    public NetworkFilter WithIncludeRemovers(bool includeRemovers) =>
        this with { IncludeRemovers = includeRemovers };

    public static NetworkFilter Parse(
        string? tier,
        int? fromYear,
        int? toYear,
        string? removers)
    {
        return new NetworkFilter(
            ParseTier(tier),
            fromYear,
            toYear,
            ParseIncludeRemovers(removers));
    }

    public string ToQueryString()
    {
        var parts = new List<string>(4);

        if (MinTier is { } minTier)
        {
            parts.Add($"tier={((int)minTier).ToString(CultureInfo.InvariantCulture)}");
        }

        if (FromYear is { } from)
        {
            parts.Add($"from={from.ToString(CultureInfo.InvariantCulture)}");
        }

        if (ToYear is { } to)
        {
            parts.Add($"to={to.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!IncludeRemovers)
        {
            parts.Add("removers=0");
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static EvidenceTier? ParseTier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var token = raw.Trim();
        if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            return numeric switch
            {
                1 => EvidenceTier.Reverted,
                2 => EvidenceTier.Contested,
                3 => EvidenceTier.Campaign,
                _ => null,
            };
        }

        return EvidenceTiers.TryParseId(token.ToLowerInvariant(), out var byId)
            ? byId
            : null;
    }

    private static bool ParseIncludeRemovers(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var token = raw.Trim();
        return !(string.Equals(token, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "off", StringComparison.OrdinalIgnoreCase));
    }
}
