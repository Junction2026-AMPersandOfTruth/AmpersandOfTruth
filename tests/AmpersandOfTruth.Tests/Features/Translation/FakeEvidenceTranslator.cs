using System.Collections.Concurrent;
using AmpersandOfTruth.Web.Features.Translation;

namespace AmpersandOfTruth.Tests.Features.Translation;

/// <summary>Scriptable in-memory <see cref="IEvidenceTranslator"/> for component and page tests.</summary>
public sealed class FakeEvidenceTranslator : IEvidenceTranslator
{
    private readonly ConcurrentDictionary<string, TranslationResult> _translations = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<AlignedTranslation> _passages = new();
    private AlignedTranslation? _defaultPassage;

    public bool FailAll { get; set; }
    public string FailureError { get; set; } = "translator unavailable";

    public int TranslateCalls { get; private set; }
    public int TranslateManyCalls { get; private set; }
    public int TranslatePassageCalls { get; private set; }
    public List<(IReadOnlyList<string> Texts, string SourceLang, string? Scope)> BatchRequests { get; } = [];

    public FakeEvidenceTranslator Returns(string original, string english)
    {
        _translations[original] = new TranslationResult(original, english, Ok: true, Error: null);
        return this;
    }

    public FakeEvidenceTranslator ReturnsFailure(string original, string error = "translation failed")
    {
        _translations[original] = new TranslationResult(original, English: null, Ok: false, Error: error);
        return this;
    }

    public FakeEvidenceTranslator ReturnsPassage(AlignedTranslation passage)
    {
        _defaultPassage = passage;
        return this;
    }

    public FakeEvidenceTranslator EnqueuePassage(AlignedTranslation passage)
    {
        _passages.Enqueue(passage);
        return this;
    }

    public Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new TranslationResult(text, string.Empty, Ok: true, Error: null));
        }

        TranslateCalls++;
        return Task.FromResult(ResolveOne(text));
    }

    public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
        IReadOnlyList<string> texts,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ct.ThrowIfCancellationRequested();

        if (texts.Count == 0 || texts.All(string.IsNullOrWhiteSpace))
        {
            return Task.FromResult<IReadOnlyList<TranslationResult>>(
                texts.Select(static t => new TranslationResult(t, string.Empty, Ok: true, Error: null)).ToArray());
        }

        TranslateManyCalls++;
        BatchRequests.Add((texts.ToArray(), sourceLang, scope));
        var results = new TranslationResult[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            var t = texts[i];
            results[i] = string.IsNullOrWhiteSpace(t)
                ? new TranslationResult(t, string.Empty, Ok: true, Error: null)
                : ResolveOne(t);
        }

        return Task.FromResult<IReadOnlyList<TranslationResult>>(results);
    }

    public Task<AlignedTranslation> TranslatePassageAsync(
        string before,
        string after,
        IReadOnlyList<(int Start, int Length)> changedInAfter,
        IReadOnlyList<(int Start, int Length)> changedInBefore,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(changedInAfter);
        ArgumentNullException.ThrowIfNull(changedInBefore);
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after))
        {
            return Task.FromResult(new AlignedTranslation(string.Empty, string.Empty, [], [], Aligned: true, Ok: true));
        }

        TranslatePassageCalls++;
        if (FailAll)
        {
            return Task.FromResult(new AlignedTranslation(string.Empty, string.Empty, [], [], Aligned: false, Ok: false, FailureError));
        }

        if (_passages.TryDequeue(out var queued))
        {
            return Task.FromResult(queued);
        }

        if (_defaultPassage is not null)
        {
            return Task.FromResult(_defaultPassage);
        }

        return Task.FromResult(new AlignedTranslation(
            BeforeEn: $"[EN] {before}",
            AfterEn: $"[EN] {after}",
            ChangedInAfterEn: changedInAfter.Select(static s => (s.Start + 5, s.Length)).ToArray(),
            ChangedInBeforeEn: changedInBefore.Select(static s => (s.Start + 5, s.Length)).ToArray(),
            Aligned: true,
            Ok: true));
    }

    private TranslationResult ResolveOne(string text)
    {
        if (FailAll)
        {
            return new TranslationResult(text, English: null, Ok: false, Error: FailureError);
        }

        return _translations.TryGetValue(text, out var scripted)
            ? scripted
            : new TranslationResult(text, $"[EN] {text}", Ok: true, Error: null);
    }
}
