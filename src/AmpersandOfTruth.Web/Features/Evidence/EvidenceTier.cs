namespace AmpersandOfTruth.Web.Features.Evidence;

/// <summary>
/// Evidence level of an incident — P0.7 (#18), <c>docs/evidence-standard.md</c>. Higher = more evidence;
/// each level requires everything below it. The enum names / <see cref="EvidenceTierInfo.Id"/>s are machine names
/// (golden set, pipeline JSON, CSS tokens) and are never shown in the UI — show <see cref="EvidenceTierInfo.Label"/>.
/// </summary>
public enum EvidenceTier
{
    Reverted = 1,
    Contested = 2,
    Campaign = 3,
}

/// <param name="Id">Stable machine id: <c>reverted | contested | campaign</c>.</param>
/// <param name="Level">1–3, shown to users as "Level n".</param>
/// <param name="Label">The exact UI wording. Only ever rendered through the shared TierBadge.</param>
/// <param name="ShortLabel">Compact form for chips, filters and chart legends.</param>
/// <param name="Meaning">One or two sentences for tooltips and the methodology page.</param>
public sealed record EvidenceTierInfo(EvidenceTier Tier, string Id, int Level, string Label, string ShortLabel, string Meaning);

/// <summary>The single source of tier wording. No ad-hoc tier strings anywhere else.</summary>
public static class EvidenceTiers
{
    public static readonly EvidenceTierInfo Reverted = new(
        EvidenceTier.Reverted, "reverted", 1,
        "Removed by Wikipedia editors", "Removed",
        "Text was added and later taken out by other Wikipedia editors. That alone does not show whether it was true or false.");

    public static readonly EvidenceTierInfo Contested = new(
        EvidenceTier.Contested, "contested", 2,
        "Disputed and removed", "Disputed",
        "A factual or narrative claim was taken out with a stated concern about accuracy, neutrality or sources, " +
        "or it kept being put back after removal.");

    public static readonly EvidenceTierInfo Campaign = new(
        EvidenceTier.Campaign, "campaign", 3,
        "Pattern consistent with a coordinated attempt", "Possible coordination",
        "The same disputed claim came back across several accounts or articles, over a long period, or together with changes to sources. " +
        "The pattern is consistent with coordination; it does not show who was behind it or why.");

    public static IReadOnlyList<EvidenceTierInfo> All { get; } = [Reverted, Contested, Campaign];

    public static EvidenceTierInfo Get(EvidenceTier tier) => tier switch
    {
        EvidenceTier.Reverted => Reverted,
        EvidenceTier.Contested => Contested,
        EvidenceTier.Campaign => Campaign,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
    };

    public static bool TryParseId(string? id, out EvidenceTier tier)
    {
        foreach (var info in All)
        {
            if (string.Equals(info.Id, id, StringComparison.Ordinal))
            {
                tier = info.Tier;
                return true;
            }
        }
        tier = default;
        return false;
    }
}
