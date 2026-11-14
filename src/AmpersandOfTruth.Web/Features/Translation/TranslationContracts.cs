namespace AmpersandOfTruth.Web.Features.Translation;

/// <summary>
/// Translates Finnish Wikipedia passages, claims, edit summaries, and fallback article titles into English (#50, #98 §3).
/// Backed by <see cref="Features.Analysis.IAnalysisModel"/> so the SQLite analysis response cache makes reruns free.
/// Never throws for model-side failures or broken alignment markers; only programmer errors and cancellation throw.
/// </summary>
public interface IEvidenceTranslator
{
    Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default);

    /// <summary>Translates a batch (e.g. edit summaries) in a single model call; result order matches input order.</summary>
    Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
        IReadOnlyList<string> texts,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default);

    /// <summary>
    /// Wraps changed spans in <c>⟦</c>…<c>⟧</c> markers, translates both sides in one call, and parses markers back
    /// into spans of the English text. If marker counts do not survive (missing, extra, nested, or unbalanced),
    /// returns the plain English translations (markers stripped) with <see cref="AlignedTranslation.Aligned"/> = <c>false</c>.
    /// </summary>
    Task<AlignedTranslation> TranslatePassageAsync(
        string before,
        string after,
        IReadOnlyList<(int Start, int Length)> changedInAfter,
        IReadOnlyList<(int Start, int Length)> changedInBefore,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default);
}

public sealed record TranslationResult(
    string Original,
    string? English,
    bool Ok,
    string? Error = null);

public sealed record AlignedTranslation(
    string BeforeEn,
    string AfterEn,
    IReadOnlyList<(int Start, int Length)> ChangedInAfterEn,
    IReadOnlyList<(int Start, int Length)> ChangedInBeforeEn,
    bool Aligned,
    bool Ok,
    string? Error = null);
