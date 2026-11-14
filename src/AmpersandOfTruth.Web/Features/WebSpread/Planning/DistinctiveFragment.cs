using System.Text.RegularExpressions;

namespace AmpersandOfTruth.Web.Features.WebSpread.Planning;

/// <summary>
/// Picks the most distinctive contiguous 5–8 word span of the inserted text (P3.2 "distinctive fragment") for an
/// exact-phrase query: a span an independent text would rarely share word for word. Rarity is pluggable — the issue's
/// ideal is low n-gram frequency in the corpus; the default is a heuristic (long words, numbers and proper nouns score
/// high, Finnish/English/Swedish function words low). Pure; thread-safe.
/// </summary>
public static partial class DistinctiveFragment
{
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        // fi
        "ja", "on", "oli", "ovat", "olivat", "ei", "se", "ne", "että", "joka", "jotka", "mutta", "tai", "kun", "myös", "sekä",
        "jo", "vain", "sen", "hän", "he", "tämä", "nämä", "kuin", "niin", "mukaan", "jälkeen", "aikana", "vuonna", "noin",
        "olla", "ollut", "sitä", "siitä", "sekä", "kanssa", "yli", "alle", "koska", "jossa", "joissa", "sillä", "eli",
        // en
        "the", "a", "an", "and", "or", "of", "in", "on", "at", "to", "for", "by", "with", "was", "were", "is", "are", "be",
        "that", "this", "it", "as", "from", "had", "has", "have", "which", "who",
        // sv
        "och", "att", "det", "som", "en", "ett", "är", "var", "av", "för", "med", "till", "på", "den", "de",
    };

    /// <summary>The span to quote, or null when the text has fewer than <paramref name="minWords"/> words.</summary>
    public static string? Pick(string? text, int minWords = 5, int maxWords = 8, Func<string, double>? rarity = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        rarity ??= DefaultRarity;

        string? best = null;
        var bestScore = double.MinValue;
        foreach (var sentence in Sentences().Split(text))
        {
            var words = Words().Matches(sentence).Select(m => m.Value).ToList();
            if (words.Count < minWords) continue;
            var scores = words.Select(w => rarity(w)).ToArray();

            for (var len = minWords; len <= Math.Min(maxWords, words.Count); len++)
            {
                for (var start = 0; start + len <= words.Count; start++)
                {
                    // Never start or end on a function word: the phrase search should anchor on content.
                    if (Stopwords.Contains(words[start]) || Stopwords.Contains(words[start + len - 1])) continue;
                    var score = 0.0;
                    for (var i = start; i < start + len; i++) score += scores[i];
                    score /= Math.Sqrt(len); // mildly prefer longer spans at equal density
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = string.Join(' ', words.Skip(start).Take(len));
                    }
                }
            }
        }

        // A short insertion (one clause) is quoted whole.
        if (best is null)
        {
            var all = Words().Matches(text).Select(m => m.Value).ToList();
            if (all.Count >= Math.Min(minWords, 3) && all.Count <= maxWords + 4) best = string.Join(' ', all);
        }
        return best;
    }

    /// <summary>The <paramref name="count"/> rarest distinct content words, in text order — a fallback keyword query.</summary>
    public static IReadOnlyList<string> Keywords(string? text, int count = 6, Func<string, double>? rarity = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        rarity ??= DefaultRarity;
        var words = Words().Matches(text).Select(m => m.Value).Where(w => !Stopwords.Contains(w))
            .DistinctBy(w => w.ToLowerInvariant()).ToList();
        var keep = words.OrderByDescending(w => rarity(w)).Take(count).ToHashSet();
        return words.Where(keep.Contains).ToList();
    }

    public static double DefaultRarity(string word)
    {
        if (Stopwords.Contains(word)) return -1;
        var score = Math.Min(word.Length, 14) / 4.0;
        if (word.Any(char.IsDigit)) score += 2.5;
        if (char.IsUpper(word[0])) score += 1.0;
        return score;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-ZÅÄÖ0-9])|\n+")]
    private static partial Regex Sentences();

    // Words keep inner hyphens/apostrophes/colons (Finnish case endings: "1939:n", "Neuvostoliitto-mielinen").
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:[-':’][\p{L}\p{N}]+)*")]
    private static partial Regex Words();
}
