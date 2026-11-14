using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// The evidence standard's <c>kept</c> exclusion, as P1.S10 (#37) wired it (<c>research/37-s10-assembly/evidence.py</c>
/// <c>longest_added_sentence</c> + <c>check_kept_in_latest_*</c>): the longest added prose sentence (≥ 25 characters) of
/// the removed edit still occurs in the page's latest revision, so the community kept the content after all.
/// S10 looks the sentence up in the raw wikitext (MariaDB <c>LOCATE</c>, case-insensitive); a sentence that carries a
/// link never matches there, so this also looks in the prose view of the latest revision (<see cref="WikitextProse"/>,
/// the same tokenizer that produced the diff's prose). Pure and thread-safe.
/// </summary>
public static class KeptInLatest
{
    public const int MinSentenceChars = 25;

    private static readonly Regex SentenceSplit = new(@"(?:[\r\n]+|\s*…\s*|(?<=[.!?])\s+)", RegexOptions.CultureInvariant);

    /// <summary>The longest added prose sentence of at least <see cref="MinSentenceChars"/>, or null.</summary>
    public static string? LongestAddedSentence(WikitextDiffResult diff)
    {
        var prose = diff.AddedProse.Length > 0
            ? diff.AddedProse
            : string.Join('\n', diff.Segments
                .Where(s => !s.Moved && s.Op == DiffOp.Added && s.Kind == SegmentKind.Prose)
                .Select(s => s.Prose.Trim()).Where(p => p.Length > 0));
        string best = "";
        foreach (var chunk in SentenceSplit.Split(prose))
        {
            var s = chunk.Trim();
            if (s.Length >= MinSentenceChars && s.Length > best.Length) best = s;
        }
        return best.Length > 0 ? best : null;
    }

    /// <summary>Whether <paramref name="sentence"/> is in the latest revision (raw wikitext or its prose).</summary>
    public static bool Contains(Latest latest, string sentence) =>
        latest.Text.Contains(sentence, StringComparison.OrdinalIgnoreCase)
        || latest.Prose.Contains(Collapse(sentence), StringComparison.OrdinalIgnoreCase);

    internal static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>One page's latest revision: its wikitext and prose view (computed once, whitespace collapsed).</summary>
    public sealed class Latest(string text)
    {
        private readonly Lazy<string> _prose = new(() => Collapse(WikitextProse.Of(text).Prose));
        public string Text { get; } = text;
        public string Prose => _prose.Value;
    }

    /// <summary>Latest revisions fetched for one run: one read per article, shared by its candidates (thread-safe).</summary>
    public sealed class RunCache(IRevisionStore revisions)
    {
        private readonly ConcurrentDictionary<long, Lazy<Task<Latest?>>> _pages = new();

        public Task<Latest?> GetAsync(long pageId, CancellationToken ct) =>
            _pages.GetOrAdd(pageId, id => new Lazy<Task<Latest?>>(() => LoadAsync(id, ct))).Value;

        private async Task<Latest?> LoadAsync(long pageId, CancellationToken ct) =>
            await revisions.LatestTextAsync(pageId, ct) is { } text ? new Latest(text) : null;
    }
}
