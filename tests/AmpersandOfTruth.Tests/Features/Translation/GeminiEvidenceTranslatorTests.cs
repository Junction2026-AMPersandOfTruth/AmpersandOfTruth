using System.Text.Json;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Translation;
using AmpersandOfTruth.Web.Features.Translation.Prompts;

namespace AmpersandOfTruth.Tests.Features.Translation;

public sealed class GeminiEvidenceTranslatorTests
{
    private readonly FakeAnalysisModel _model = new();
    private readonly GeminiEvidenceTranslator _translator;

    public GeminiEvidenceTranslatorTests()
    {
        _translator = new GeminiEvidenceTranslator(_model);
    }

    [Fact]
    public async Task TranslateAsync_returns_english_and_passes_scope_and_source_lang()
    {
        _model.Returns(
            TranslateEvidencePrompt.Id,
            """{"translations":["Finland started the Winter War."]}""");

        var result = await _translator.TranslateAsync(
            "Suomi aloitti talvisodan.",
            sourceLang: "fi",
            scope: "inv-42");

        Assert.True(result.Ok);
        Assert.Equal("Suomi aloitti talvisodan.", result.Original);
        Assert.Equal("Finland started the Winter War.", result.English);
        Assert.Null(result.Error);

        var req = Assert.Single(_model.Requests);
        Assert.Equal(TranslateEvidencePrompt.Id, req.PromptId);
        Assert.Equal("inv-42", req.Scope);
        Assert.Equal("fi", req.Variables["source_lang"]);
        Assert.Equal("""["Suomi aloitti talvisodan."]""", req.Variables["items"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0009\u000D\u000A")]
    public async Task TranslateAsync_empty_or_whitespace_returns_ok_empty_without_calling_model(string input)
    {
        var result = await _translator.TranslateAsync(input);

        Assert.True(result.Ok);
        Assert.Equal(input, result.Original);
        Assert.Equal(string.Empty, result.English);
        Assert.Null(result.Error);
        Assert.Empty(_model.Requests);
    }

    [Theory]
    [InlineData(AnalysisOutcome.Failed, "HTTP 503")]
    [InlineData(AnalysisOutcome.Blocked, "blocked: safety")]
    [InlineData(AnalysisOutcome.Invalid, "schema mismatch")]
    [InlineData(AnalysisOutcome.Truncated, "max tokens")]
    [InlineData(AnalysisOutcome.BudgetExceeded, "daily cap reached")]
    public async Task TranslateAsync_maps_every_non_ok_outcome_to_ok_false_without_throwing(
        AnalysisOutcome outcome,
        string error)
    {
        _model.Returns(TranslateEvidencePrompt.Id, outcome, error);

        var result = await _translator.TranslateAsync("Kumottu muokkaus");

        Assert.False(result.Ok);
        Assert.Equal("Kumottu muokkaus", result.Original);
        Assert.Null(result.English);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task TranslateManyAsync_preserves_order_skips_blank_items_and_makes_single_model_call()
    {
        _model.Returns(
            TranslateEvidencePrompt.Id,
            """{"translations":["Unsourced claim removed","Reverted to previous version","Fixed year"]}""");

        var inputs = new[]
        {
            "Lähteetön väite poistettu",
            "",
            "Palautettu aikaisempaan versioon",
            "   ",
            "Korjattu vuosiluku",
        };

        var results = await _translator.TranslateManyAsync(inputs, scope: "inc-7");

        Assert.Single(_model.Requests);
        Assert.Equal(
            """["Lähteetön väite poistettu","Palautettu aikaisempaan versioon","Korjattu vuosiluku"]""",
            _model.Requests[0].Variables["items"]);

        Assert.Equal(5, results.Count);
        Assert.Equal(new TranslationResult("Lähteetön väite poistettu", "Unsourced claim removed", true, null), results[0]);
        Assert.Equal(new TranslationResult("", "", true, null), results[1]);
        Assert.Equal(new TranslationResult("Palautettu aikaisempaan versioon", "Reverted to previous version", true, null), results[2]);
        Assert.Equal(new TranslationResult("   ", "", true, null), results[3]);
        Assert.Equal(new TranslationResult("Korjattu vuosiluku", "Fixed year", true, null), results[4]);
    }

    [Fact]
    public async Task TranslateManyAsync_empty_or_all_whitespace_makes_zero_model_calls()
    {
        Assert.Empty(await _translator.TranslateManyAsync([]));

        var blanks = await _translator.TranslateManyAsync(["", "  "]);
        Assert.Equal(2, blanks.Count);
        Assert.All(blanks, r =>
        {
            Assert.True(r.Ok);
            Assert.Equal(string.Empty, r.English);
        });
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task TranslateManyAsync_wrong_count_from_model_degrades_to_ok_false_without_throwing()
    {
        _model.Returns(TranslateEvidencePrompt.Id, """{"translations":["Only one"]}""");

        var results = await _translator.TranslateManyAsync(["Ensimmäinen", "Toinen"]);

        Assert.Equal(2, results.Count);
        Assert.All(results, r =>
        {
            Assert.False(r.Ok);
            Assert.Null(r.English);
            Assert.Contains("expected 2", r.Error);
        });
    }

    [Fact]
    public async Task TranslatePassageAsync_wraps_markers_and_maps_english_spans_to_exact_substrings()
    {
        const string before = "Neuvostoliitto hyökkäsi Suomeen 30. marraskuuta 1939.";
        const string after = "Talvisota alkoi, kun fasistinen Suomi hyökkäsi Neuvostoliittoon vuonna 1939.";

        var beforeSpan = SpanOf(before, "Neuvostoliitto hyökkäsi Suomeen");
        var afterSpan1 = SpanOf(after, "fasistinen Suomi");
        var afterSpan2 = SpanOf(after, "Neuvostoliittoon");

        _model.Returns(
            TranslatePassagePrompt.Id,
            JsonSerializer.Serialize(
                new TranslatePassageOutput(
                    BeforeEn: "⟦The Soviet Union invaded Finland⟧ on 30 November 1939.",
                    AfterEn: "The Winter War began when ⟦fascist Finland⟧ attacked ⟦the Soviet Union⟧ in 1939."),
                AnalysisJson.Options));

        var result = await _translator.TranslatePassageAsync(
            before,
            after,
            changedInAfter: [afterSpan2, afterSpan1], // unsorted input spans should still sort cleanly
            changedInBefore: [beforeSpan],
            scope: "inc-mainila");

        var req = Assert.Single(_model.Requests);
        Assert.Equal(TranslatePassagePrompt.Id, req.PromptId);
        Assert.Equal("inc-mainila", req.Scope);
        Assert.Equal("⟦Neuvostoliitto hyökkäsi Suomeen⟧ 30. marraskuuta 1939.", req.Variables["before"]);
        Assert.Equal("Talvisota alkoi, kun ⟦fasistinen Suomi⟧ hyökkäsi ⟦Neuvostoliittoon⟧ vuonna 1939.", req.Variables["after"]);

        Assert.True(result.Ok);
        Assert.True(result.Aligned);
        Assert.Null(result.Error);
        Assert.Equal("The Soviet Union invaded Finland on 30 November 1939.", result.BeforeEn);
        Assert.Equal("The Winter War began when fascist Finland attacked the Soviet Union in 1939.", result.AfterEn);

        var mappedBefore = Assert.Single(result.ChangedInBeforeEn);
        Assert.Equal("The Soviet Union invaded Finland", result.BeforeEn.Substring(mappedBefore.Start, mappedBefore.Length));

        Assert.Equal(2, result.ChangedInAfterEn.Count);
        Assert.Equal("fascist Finland", result.AfterEn.Substring(result.ChangedInAfterEn[0].Start, result.ChangedInAfterEn[0].Length));
        Assert.Equal("the Soviet Union", result.AfterEn.Substring(result.ChangedInAfterEn[1].Start, result.ChangedInAfterEn[1].Length));
    }

    [Theory]
    [InlineData("The Soviet Union invaded Finland.", "The Winter War began when fascist Finland attacked.")] // missing marker
    [InlineData("The Soviet Union invaded Finland.", "The Winter War began when ⟦fascist Finland attacked.")] // unclosed marker
    [InlineData("The Soviet Union invaded Finland.", "The Winter War began when fascist Finland⟧ attacked.")] // unmatched close
    [InlineData("The Soviet Union invaded Finland.", "The Winter War began ⟦when ⟦fascist⟧ Finland⟧ attacked.")] // nested markers
    [InlineData("The Soviet Union invaded Finland.", "The Winter War began ⟦fascist⟧ ⟦Finland⟧ attacked.")] // extra marker
    [InlineData("The Soviet Union invaded Finland.", "The Winter War began ⟦⟧ when fascist Finland attacked.")] // empty marker
    [InlineData("⟦The Soviet Union⟧ invaded Finland.", "The Winter War began when ⟦fascist Finland⟧ attacked.")] // unexpected marker in before
    public async Task TranslatePassageAsync_broken_markers_return_plain_text_with_aligned_false_and_never_throw(
        string modelBeforeEn,
        string modelAfterEn)
    {
        const string before = "Neuvostoliitto hyökkäsi Suomeen.";
        const string after = "Talvisota alkoi, kun fasistinen Suomi hyökkäsi.";
        var afterSpan = SpanOf(after, "fasistinen Suomi");

        _model.Returns(
            TranslatePassagePrompt.Id,
            JsonSerializer.Serialize(new TranslatePassageOutput(modelBeforeEn, modelAfterEn), AnalysisJson.Options));

        var result = await _translator.TranslatePassageAsync(
            before,
            after,
            changedInAfter: [afterSpan],
            changedInBefore: []);

        Assert.True(result.Ok);
        Assert.False(result.Aligned);
        Assert.Null(result.Error);
        Assert.Empty(result.ChangedInBeforeEn);
        Assert.Empty(result.ChangedInAfterEn);
        Assert.DoesNotContain(GeminiEvidenceTranslator.MarkerOpen, result.BeforeEn);
        Assert.DoesNotContain(GeminiEvidenceTranslator.MarkerClose, result.BeforeEn);
        Assert.DoesNotContain(GeminiEvidenceTranslator.MarkerOpen, result.AfterEn);
        Assert.DoesNotContain(GeminiEvidenceTranslator.MarkerClose, result.AfterEn);
        Assert.NotEmpty(result.AfterEn);
    }

    [Fact]
    public async Task TranslatePassageAsync_sanitizes_pre_existing_marker_chars_in_source_text()
    {
        const string before = "Teksti ⟦a⟧ ennen.";
        const string after = "Teksti ⟦a⟧ ja lisäys jälkeen.";
        var addedSpan = SpanOf(after, "ja lisäys");

        _model.Returns(
            TranslatePassagePrompt.Id,
            JsonSerializer.Serialize(
                new TranslatePassageOutput("Text [a] before.", "Text [a] ⟦and addition⟧ after."),
                AnalysisJson.Options));

        var result = await _translator.TranslatePassageAsync(before, after, [addedSpan], []);

        var req = Assert.Single(_model.Requests);
        Assert.Equal("Teksti [a] ennen.", req.Variables["before"]);
        Assert.Equal("Teksti [a] ⟦ja lisäys⟧ jälkeen.", req.Variables["after"]);
        Assert.True(result.Aligned);
        var span = Assert.Single(result.ChangedInAfterEn);
        Assert.Equal("and addition", result.AfterEn.Substring(span.Start, span.Length));
    }

    [Fact]
    public async Task TranslatePassageAsync_both_sides_blank_skips_model_call()
    {
        var result = await _translator.TranslatePassageAsync("", "   ", [], []);

        Assert.True(result.Ok);
        Assert.True(result.Aligned);
        Assert.Equal(string.Empty, result.BeforeEn);
        Assert.Equal(string.Empty, result.AfterEn);
        Assert.Empty(_model.Requests);
    }

    [Fact]
    public async Task TranslatePassageAsync_model_failure_returns_ok_false_without_throwing()
    {
        _model.Returns(TranslatePassagePrompt.Id, AnalysisOutcome.Failed, "timeout");

        var result = await _translator.TranslatePassageAsync("Ennen", "Jälkeen", [(0, 7)], [(0, 5)]);

        Assert.False(result.Ok);
        Assert.False(result.Aligned);
        Assert.Equal(string.Empty, result.BeforeEn);
        Assert.Equal(string.Empty, result.AfterEn);
        Assert.Empty(result.ChangedInAfterEn);
        Assert.Empty(result.ChangedInBeforeEn);
        Assert.Equal("timeout", result.Error);
    }

    private static (int Start, int Length) SpanOf(string text, string substring)
    {
        var idx = text.IndexOf(substring, StringComparison.Ordinal);
        Assert.True(idx >= 0, $"Substring '{substring}' not found in '{text}'.");
        return (idx, substring.Length);
    }
}
