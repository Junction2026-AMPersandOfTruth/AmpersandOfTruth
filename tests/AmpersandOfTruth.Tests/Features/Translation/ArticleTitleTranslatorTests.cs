using AmpersandOfTruth.Web.Features.Translation;

namespace AmpersandOfTruth.Tests.Features.Translation;

public sealed class ArticleTitleTranslatorTests
{
    [Fact]
    public async Task TranslateMissingAsync_batches_only_untitled_distinct_articles_in_single_call_with_scope()
    {
        var translator = new FakeEvidenceTranslator()
            .Returns("Suomen sisällissodan nimet", "Names of the Finnish Civil War")
            .Returns("Punakaarti", "Red Guards");

        var articles = new (string TitleFi, string? TitleEn)[]
        {
            ("Suomen sisällissota", "Finnish Civil War"),
            ("Suomen sisällissodan nimet", null),
            ("Punakaarti", null),
            ("Suomen sisällissodan nimet", null),
            ("Mannerheim", "Mannerheim"),
        };

        var result = await ArticleTitleTranslator.TranslateMissingAsync(
            translator,
            articles,
            scope: "civil-war-1918");

        Assert.Equal(1, translator.TranslateManyCalls);
        var batch = Assert.Single(translator.BatchRequests);
        Assert.Equal(["Suomen sisällissodan nimet", "Punakaarti"], batch.Texts);
        Assert.Equal("fi", batch.SourceLang);
        Assert.Equal("civil-war-1918", batch.Scope);

        Assert.Equal(2, result.Count);
        Assert.Equal("Names of the Finnish Civil War", result["Suomen sisällissodan nimet"]);
        Assert.Equal("Red Guards", result["Punakaarti"]);
    }

    [Fact]
    public async Task TranslateMissingAsync_skips_call_when_all_have_sibling_or_existing_translation()
    {
        var translator = new FakeEvidenceTranslator();
        var existing = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Terijoen hallitus"] = "Terijoki Government",
        };

        var result = await ArticleTitleTranslator.TranslateMissingAsync(
            translator,
            [("Talvisota", "Winter War"), ("Terijoen hallitus", null)],
            existing: existing,
            scope: "winter-war");

        Assert.Empty(result);
        Assert.Equal(0, translator.TranslateManyCalls);
    }

    [Fact]
    public async Task TranslateMissingAsync_omits_failed_items_and_never_throws_on_translator_exception()
    {
        var translator = new FakeEvidenceTranslator()
            .ReturnsFailure("Epäonnistuva otsikko")
            .Returns("Onnistuva otsikko", "Succeeded Title");

        var partial = await ArticleTitleTranslator.TranslateMissingAsync(
            translator,
            [("Epäonnistuva otsikko", null), ("Onnistuva otsikko", null)],
            scope: "winter-war");

        Assert.Single(partial);
        Assert.Equal("Succeeded Title", partial["Onnistuva otsikko"]);
        Assert.False(partial.ContainsKey("Epäonnistuva otsikko"));

        var throwingResult = await ArticleTitleTranslator.TranslateMissingAsync(
            new ThrowingTranslator(),
            [("Suomen sisällissodan nimet", null)],
            scope: "civil-war-1918");

        Assert.Empty(throwingResult);
    }

    private sealed class ThrowingTranslator : IEvidenceTranslator
    {
        public Task<TranslationResult> TranslateAsync(string text, string sourceLang = "fi", string? scope = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task<IReadOnlyList<TranslationResult>> TranslateManyAsync(IReadOnlyList<string> texts, string sourceLang = "fi", string? scope = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task<AlignedTranslation> TranslatePassageAsync(
            string before,
            string after,
            IReadOnlyList<(int Start, int Length)> changedInAfter,
            IReadOnlyList<(int Start, int Length)> changedInBefore,
            string sourceLang = "fi",
            string? scope = null,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}
