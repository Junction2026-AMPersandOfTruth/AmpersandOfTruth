namespace AmpersandOfTruth.Web.Features.Evidence;

using System.Globalization;

/// <summary>
/// S11 (#38) evaluation figures on the sealed test split (<c>research/38-s11-decision/README.md</c>).
/// Single source of truth for <c>/methodology</c> and any quality badge.
/// </summary>
public static class MeasuredQuality
{
    public static DateOnly MeasuredOn { get; } = new(2026, 11, 14);

    public const string SourcePath = "research/38-s11-decision/README.md";

    public static Uri SourceUrl { get; } =
        new($"https://github.com/Keymasterer44/SomeMysteriousAppForJunction/blob/main/{SourcePath}");

    public const int HeldOutTopics = 4;

    public const int ExpectedDisputedItems = 26;

    /// <summary>Of the first 10 results, share that are real disputed insertions (judged strict precision @10).</summary>
    public const double PrecisionAt10 = 0.85;

    /// <summary>Simple word-only / naive baseline on the same top-10 measure.</summary>
    public const double BaselinePrecisionAt10 = 0.25;

    /// <summary>Of the first 25 results, share that are real disputed insertions.</summary>
    public const double PrecisionAt25 = 0.56;

    /// <summary>Share of known disputed insertions we find.</summary>
    public const double Recall = 0.50;

    /// <summary>Exact evidence-level match rate on matched items.</summary>
    public const double LevelAccuracy = 0.56;

    /// <summary>Results shown at a higher level than the evidence supports.</summary>
    public const double OverclaimRate = 0.33;

    /// <summary>Duplicate findings across results.</summary>
    public const int Duplicates = 0;

    public static string FormatPercent(double ratio) =>
        $"{Math.Round(ratio * 100, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)}%";

    public static string FormatCount(int count) =>
        count.ToString("N0", CultureInfo.InvariantCulture);

    public static string FormatMeasuredOn() =>
        MeasuredOn.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
