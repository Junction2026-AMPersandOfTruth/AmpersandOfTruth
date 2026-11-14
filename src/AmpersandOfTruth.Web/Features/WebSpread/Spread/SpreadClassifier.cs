using AmpersandOfTruth.Web.Features.WebSpread.Matching;

namespace AmpersandOfTruth.Web.Features.WebSpread.Spread;

/// <summary>P3.6 bucket logic and the headline sentence. Day granularity (timezones), windows inclusive. Pure; thread-safe.</summary>
public static class SpreadClassifier
{
    /// <param name="today">Closes a still-open window (claim never removed).</param>
    public static (SpreadBucket Bucket, bool NearBoundary) Bucket(DateOnly? date, IReadOnlyList<ExposureWindow> windows, DateOnly today)
    {
        if (date is not { } d || windows.Count == 0) return (SpreadBucket.Undated, false);

        var spans = windows.Select(w => (From: ExposureWindows.Day(w.From), To: w.Until is { } u ? ExposureWindows.Day(u) : today)).ToList();
        var near = spans.Any(s => Math.Abs(d.DayNumber - s.From.DayNumber) <= 1 || Math.Abs(d.DayNumber - s.To.DayNumber) <= 1);

        if (d < spans[0].From) return (SpreadBucket.Before, near);
        if (spans.Any(s => d >= s.From && d <= s.To)) return (SpreadBucket.During, near);
        return (SpreadBucket.AfterRemoval, near);
    }

    /// <summary>The upgrade rule: only with wording overlap, a Wikipedia credit/link, or a mirror — never from timing alone.</summary>
    public static bool LikelyFromWikipedia(SpreadBucket bucket, ClaimMatch match) =>
        bucket is SpreadBucket.During or SpreadBucket.AfterRemoval
        && match.Stance != Stance.Refutes
        && (match.MatchType is MatchKind.VerbatimCopy or MatchKind.CloseParaphrase
            || match.CitesWikipedia
            || match.SourceType == SourceType.WikipediaMirror);

    /// <summary>Sources that carry the claim (assert or repeat it), as opposed to refuting or merely mentioning it.</summary>
    public static bool Carries(WebSpreadSource s) =>
        s.Stance is Stance.Asserts or Stance.RepeatsWithAttribution
        || (s.Stance == Stance.Unknown && s.MatchType != MatchKind.None);

    public static WebSpreadSource? Earliest(IEnumerable<WebSpreadSource> sources) =>
        sources.Where(s => Carries(s) && s.Bucket != SpreadBucket.Undated && s.Dating.EffectiveDate is not null)
            .OrderBy(s => s.Dating.EffectiveDate).ThenBy(s => s.Dating.Confidence == Dating.DateConfidence.Low)
            .FirstOrDefault();

    /// <summary>E.g. "Found on 3 sites before the Wikipedia edit and 11 after it; 4 of the later ones reuse Wikipedia's wording or credit it."</summary>
    public static string Headline(IReadOnlyList<WebSpreadSource> sources, int searches, IReadOnlyCollection<string> languages)
    {
        var carrying = sources.Where(Carries).ToList();
        var refuting = sources.Count(s => s.IsRefutation);
        if (carrying.Count == 0)
        {
            var searched = $"{searches} {(searches == 1 ? "search" : "searches")} in {Join(languages.Select(LanguageName).ToList())}";
            return refuting > 0
                ? $"No page repeating this claim was found ({searched}); {Sites(refuting)} {Verb(refuting)} against it."
                : $"No page carrying this claim was found ({searched}).";
        }

        var before = carrying.Count(s => s.Bucket == SpreadBucket.Before);
        var after = carrying.Count(s => s.Bucket is SpreadBucket.During or SpreadBucket.AfterRemoval);
        var undated = carrying.Count(s => s.Bucket == SpreadBucket.Undated);
        var reuse = carrying.Count(s => s.LikelyFromWikipedia);

        var parts = new List<string>();
        if (before > 0) parts.Add($"{Sites(before)} before the Wikipedia edit");
        if (after > 0) parts.Add(before > 0 ? $"{Sites(after)} after it" : $"{Sites(after)} after the Wikipedia edit");
        if (undated > 0) parts.Add($"{Sites(undated)} without a reliable date");
        var sentence = "Found on " + Join(parts);
        if (reuse > 0) sentence += reuse == 1
            ? "; 1 of the later ones reuses Wikipedia's wording or credits it"
            : $"; {reuse} of the later ones reuse Wikipedia's wording or credit it";
        if (refuting > 0) sentence += $". {Sites(refuting, capital: true)} {Verb(refuting)} against it";
        return sentence + ".";
    }

    public static string LanguageName(string code) => code switch
    {
        "fi" => "Finnish",
        "en" => "English",
        "sv" => "Swedish",
        "ru" => "Russian",
        _ => code,
    };

    private static string Verb(int n) => n == 1 ? "argues" : "argue";

    private static string Sites(int n, bool capital = false) => n == 1 ? (capital ? "One site" : "1 site") : $"{n} sites";

    private static string Join(List<string> parts) => parts.Count switch
    {
        0 => "",
        1 => parts[0],
        _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
    };
}
