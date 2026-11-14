namespace AmpersandOfTruth.Web.Features.Hotspots;

public interface IHotspotStore
{
    /// <summary>Loads precomputed hotspot aggregates, or <c>null</c> when <c>research.hotspot_*</c> tables are missing.</summary>
    Task<HotspotData?> LoadAsync(CancellationToken ct = default);
}

public sealed record HotspotData(
    HotspotHeadline Headline,
    IReadOnlyList<HotArticle> Articles,
    IReadOnlyList<HotMonth> Months,
    IReadOnlyList<HotCategory> Categories);

public sealed record HotspotHeadline(
    long Revisions,
    int Articles,
    int Reverts,
    int Episodes,
    double AnonRevertedShare);

public sealed record HotArticle(
    long PageId,
    string TitleFi,
    string? TitleEn,
    double ContestScore,
    int Reverts,
    int RevertsDispute,
    int RevertsVandalism,
    int RevertsOther,
    int Episodes,
    double AnonShare,
    DateTime FirstTs,
    DateTime LastTs,
    IReadOnlyList<int> YearlyReverts)
{
    public bool HasDistinctEnglishTitle =>
        !string.IsNullOrWhiteSpace(TitleEn) &&
        !string.Equals(TitleEn, TitleFi, StringComparison.OrdinalIgnoreCase);

    public string DisplayTitle => HasDistinctEnglishTitle ? TitleEn! : TitleFi;
}

public sealed record HotMonth(
    string Month,
    int Reverts,
    int Episodes,
    int Edits,
    long? TopPageId,
    string? TopTitleFi,
    string? TopTitleEn)
{
    public string? TopDisplayTitle =>
        !string.IsNullOrWhiteSpace(TopTitleEn)
            ? TopTitleEn
            : (!string.IsNullOrWhiteSpace(TopTitleFi) ? TopTitleFi : null);
}

public sealed record HotCategory(
    string Category,
    int Articles,
    double ContestScore,
    int Reverts,
    int DisputeReverts,
    int Episodes);

public sealed record HotYear(
    int Year,
    int Reverts,
    int Episodes,
    int Edits,
    long? TopPageId,
    string? TopTitleFi,
    string? TopTitleEn)
{
    public string? TopDisplayTitle =>
        !string.IsNullOrWhiteSpace(TopTitleEn)
            ? TopTitleEn
            : (!string.IsNullOrWhiteSpace(TopTitleFi) ? TopTitleFi : null);
}

public sealed record HotSpike(
    int Year,
    int Reverts,
    int Episodes,
    int Edits,
    long? PageId,
    string TitleFi,
    string? TitleEn)
{
    public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(TitleEn) ? TitleEn : TitleFi;
}
