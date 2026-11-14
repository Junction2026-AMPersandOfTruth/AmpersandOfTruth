using System.Text;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Translation.Prompts;

namespace AmpersandOfTruth.Web.Features.Translation;

/// <summary>
/// <see cref="IEvidenceTranslator"/> over <see cref="IAnalysisModel"/> (job <see cref="AnalysisJob.Translate"/>).
/// Singleton and stateless; caching is handled by <c>AnalysisCache</c> inside <see cref="IAnalysisModel"/>.
/// </summary>
public sealed class GeminiEvidenceTranslator(IAnalysisModel model) : IEvidenceTranslator
{
    public const char MarkerOpen = '⟦';
    public const char MarkerClose = '⟧';

    public async Task<TranslationResult> TranslateAsync(
        string text,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return new TranslationResult(text, string.Empty, Ok: true, Error: null);
        }

        var batch = await TranslateManyAsync([text], sourceLang, scope, ct);
        return batch[0];
    }

    public async Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(
        IReadOnlyList<string> texts,
        string sourceLang = "fi",
        string? scope = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            return [];
        }

        var results = new TranslationResult[texts.Count];
        var nonEmptyIndices = new List<int>(texts.Count);
        var nonEmptyTexts = new List<string>(texts.Count);

        for (var i = 0; i < texts.Count; i++)
        {
            var item = texts[i] ?? throw new ArgumentNullException(nameof(texts), $"Element at index {i} is null.");
            if (string.IsNullOrWhiteSpace(item))
            {
                results[i] = new TranslationResult(item, string.Empty, Ok: true, Error: null);
            }
            else
            {
                nonEmptyIndices.Add(i);
                nonEmptyTexts.Add(item);
            }
        }

        if (nonEmptyTexts.Count == 0)
        {
            return results;
        }

        var request = new AnalysisRequest(
            TranslateEvidencePrompt.Id,
            TranslateEvidencePrompt.Variables(NormalizeSourceLang(sourceLang), nonEmptyTexts))
        {
            Scope = scope,
        };

        var reply = await model.RunAsync<TranslateEvidenceOutput>(request, ct);
        if (!reply.IsOk)
        {
            var error = DescribeFailure(reply.Outcome, reply.Error);
            foreach (var index in nonEmptyIndices)
            {
                results[index] = new TranslationResult(texts[index], English: null, Ok: false, Error: error);
            }

            return results;
        }

        if (reply.Value.Translations.Count != nonEmptyTexts.Count)
        {
            var countError = $"expected {nonEmptyTexts.Count} translation(s), got {reply.Value.Translations.Count}";
            foreach (var index in nonEmptyIndices)
            {
                results[index] = new TranslationResult(texts[index], English: null, Ok: false, Error: countError);
            }

            return results;
        }

        for (var k = 0; k < nonEmptyIndices.Count; k++)
        {
            var index = nonEmptyIndices[k];
            results[index] = new TranslationResult(texts[index], reply.Value.Translations[k], Ok: true, Error: null);
        }

        return results;
    }

    public async Task<AlignedTranslation> TranslatePassageAsync(
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

        if (string.IsNullOrWhiteSpace(before) && string.IsNullOrWhiteSpace(after))
        {
            return new AlignedTranslation(
                BeforeEn: string.Empty,
                AfterEn: string.Empty,
                ChangedInAfterEn: [],
                ChangedInBeforeEn: [],
                Aligned: true,
                Ok: true,
                Error: null);
        }

        var (markedBefore, expectedBeforeCount) = InsertMarkers(before, changedInBefore);
        var (markedAfter, expectedAfterCount) = InsertMarkers(after, changedInAfter);

        var request = new AnalysisRequest(
            TranslatePassagePrompt.Id,
            TranslatePassagePrompt.Variables(NormalizeSourceLang(sourceLang), markedBefore, markedAfter))
        {
            Scope = scope,
        };

        var reply = await model.RunAsync<TranslatePassageOutput>(request, ct);
        if (!reply.IsOk)
        {
            return new AlignedTranslation(
                BeforeEn: string.Empty,
                AfterEn: string.Empty,
                ChangedInAfterEn: [],
                ChangedInBeforeEn: [],
                Aligned: false,
                Ok: false,
                Error: DescribeFailure(reply.Outcome, reply.Error));
        }

        var parsedBefore = ExtractMarkers(reply.Value.BeforeEn, expectedBeforeCount);
        var parsedAfter = ExtractMarkers(reply.Value.AfterEn, expectedAfterCount);
        var aligned = parsedBefore.Aligned && parsedAfter.Aligned;

        return aligned
            ? new AlignedTranslation(
                BeforeEn: parsedBefore.PlainText,
                AfterEn: parsedAfter.PlainText,
                ChangedInAfterEn: parsedAfter.Spans,
                ChangedInBeforeEn: parsedBefore.Spans,
                Aligned: true,
                Ok: true,
                Error: null)
            : new AlignedTranslation(
                BeforeEn: parsedBefore.PlainText,
                AfterEn: parsedAfter.PlainText,
                ChangedInAfterEn: [],
                ChangedInBeforeEn: [],
                Aligned: false,
                Ok: true,
                Error: null);
    }

    internal static (string MarkedText, int MarkerCount) InsertMarkers(
        string text,
        IReadOnlyList<(int Start, int Length)> spans)
    {
        var sanitized = SanitizePreExistingMarkers(text);
        if (spans.Count == 0 || sanitized.Length == 0)
        {
            return (sanitized, 0);
        }

        var clamped = new List<(int Start, int End)>(spans.Count);
        foreach (var (rawStart, rawLength) in spans)
        {
            if (rawLength <= 0)
            {
                continue;
            }

            var start = Math.Clamp(rawStart, 0, sanitized.Length);
            var end = (int)Math.Clamp((long)rawStart + rawLength, start, sanitized.Length);
            if (end > start)
            {
                clamped.Add((start, end));
            }
        }

        if (clamped.Count == 0)
        {
            return (sanitized, 0);
        }

        clamped.Sort(static (a, b) =>
        {
            var startCmp = a.Start.CompareTo(b.Start);
            return startCmp != 0 ? startCmp : b.End.CompareTo(a.End);
        });

        var merged = new List<(int Start, int End)>(clamped.Count) { clamped[0] };
        for (var i = 1; i < clamped.Count; i++)
        {
            var current = clamped[i];
            var last = merged[^1];
            if (current.Start < last.End)
            {
                merged[^1] = (last.Start, Math.Max(last.End, current.End));
            }
            else
            {
                merged.Add(current);
            }
        }

        var sb = new StringBuilder(sanitized.Length + merged.Count * 2);
        var cursor = 0;
        foreach (var (start, end) in merged)
        {
            sb.Append(sanitized, cursor, start - cursor);
            sb.Append(MarkerOpen);
            sb.Append(sanitized, start, end - start);
            sb.Append(MarkerClose);
            cursor = end;
        }

        sb.Append(sanitized, cursor, sanitized.Length - cursor);
        return (sb.ToString(), merged.Count);
    }

    internal static (string PlainText, IReadOnlyList<(int Start, int Length)> Spans, bool Aligned) ExtractMarkers(
        string text,
        int expectedCount)
    {
        var plain = new StringBuilder(text.Length);
        var spans = new List<(int Start, int Length)>(Math.Max(0, expectedCount));
        int? openStart = null;
        var valid = true;

        foreach (var ch in text)
        {
            if (ch == MarkerOpen)
            {
                if (openStart is not null)
                {
                    valid = false;
                }
                else
                {
                    openStart = plain.Length;
                }
            }
            else if (ch == MarkerClose)
            {
                if (openStart is null)
                {
                    valid = false;
                }
                else
                {
                    var length = plain.Length - openStart.Value;
                    if (length == 0)
                    {
                        valid = false;
                    }
                    else
                    {
                        spans.Add((openStart.Value, length));
                    }

                    openStart = null;
                }
            }
            else
            {
                plain.Append(ch);
            }
        }

        if (openStart is not null)
        {
            valid = false;
        }

        var aligned = valid && spans.Count == expectedCount;
        return (plain.ToString(), aligned ? spans : [], aligned);
    }

    private static string SanitizePreExistingMarkers(string text) =>
        text.IndexOfAny([MarkerOpen, MarkerClose]) < 0
            ? text
            : text.Replace(MarkerOpen, '[').Replace(MarkerClose, ']');

    private static string NormalizeSourceLang(string? sourceLang) =>
        string.IsNullOrWhiteSpace(sourceLang) ? "fi" : sourceLang.Trim();

    private static string DescribeFailure(AnalysisOutcome outcome, string? error) =>
        !string.IsNullOrWhiteSpace(error) ? error : outcome.ToString().ToLowerInvariant();
}
