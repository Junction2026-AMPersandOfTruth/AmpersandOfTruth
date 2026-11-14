namespace AmpersandOfTruth.Web.Features.Translation;

/// <summary>
/// Batch helper for translating Finnish article titles that lack an English sibling title into English
/// in a single <see cref="IEvidenceTranslator.TranslateManyAsync"/> call.
/// </summary>
public static class ArticleTitleTranslator
{
    public static async Task<IReadOnlyDictionary<string, string>> TranslateMissingAsync(
        IEvidenceTranslator? translator,
        IEnumerable<(string TitleFi, string? TitleEn)> articles,
        IReadOnlyDictionary<string, string>? existing = null,
        string? scope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(articles);

        if (translator is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var pending = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (titleFi, titleEn) in articles)
        {
            if (!string.IsNullOrWhiteSpace(titleEn) || string.IsNullOrWhiteSpace(titleFi))
            {
                continue;
            }

            if (existing is not null && existing.ContainsKey(titleFi))
            {
                continue;
            }

            if (seen.Add(titleFi))
            {
                pending.Add(titleFi);
            }
        }

        if (pending.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        try
        {
            var results = await translator.TranslateManyAsync(pending, sourceLang: "fi", scope: scope, ct: ct);
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            var count = Math.Min(pending.Count, results.Count);
            for (var i = 0; i < count; i++)
            {
                var r = results[i];
                if (r.Ok && !string.IsNullOrWhiteSpace(r.English))
                {
                    map[pending[i]] = r.English;
                }
            }

            return map;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
