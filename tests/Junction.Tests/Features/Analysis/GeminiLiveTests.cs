using Junction.Tests.Infrastructure;
using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Prompts;
using Microsoft.Extensions.DependencyInjection;

namespace Junction.Tests.Features.Analysis;

/// <summary>
/// Real calls to Gemini through the app's DI (real config, real prompts, a throwaway SQLite cache). Costs a few cents.
/// Skipped unless <c>GEMINI_TESTS=1</c>. Run:
/// <c>GEMINI_TESTS=1 dotnet test --filter Category=Gemini</c> (key from user-secrets <c>Gemini:ApiKey</c> or env <c>Gemini__ApiKey</c>).
/// </summary>
[Trait("Category", "Gemini")]
public sealed class GeminiLiveTests(AppFactory app) : IClassFixture<AppFactory>
{
    private static AnalysisRequest MainilaEdit(string scope) => new(ClassifyEditPrompt.Id, ClassifyEditPrompt.Variables(
        articleTitle: "Talvisota",
        editTimestamp: new DateTimeOffset(2014, 3, 1, 12, 0, 0, TimeSpan.Zero),
        editorKind: "anonymous",
        exposure: "7 min",
        editSummary: "",
        revertSummary: "Kumottu muokkaus, lähteetön",
        removedText: "Neuvostoliitto hyökkäsi Suomeen 30. marraskuuta 1939 ilman sodanjulistusta.",
        insertedText: "Suomi aloitti talvisodan ampumalla Mainilan kylään, minkä vuoksi Neuvostoliiton oli pakko puolustautua.",
        context: "'''Talvisota''' oli Neuvostoliiton ja Suomen välinen sota, joka käytiin 30. marraskuuta 1939 – 13. maaliskuuta 1940."))
    { Scope = scope };

    private IAnalysisModel Model => app.Services.GetRequiredService<IAnalysisModel>();

    [GeminiFact]
    public async Task Sample_edit_returns_schema_valid_typed_output_then_hits_the_cache()
    {
        var request = MainilaEdit($"live-test-{Guid.NewGuid():N}");

        var first = await Model.RunAsync<EditClassification>(request);

        Assert.True(first.IsOk, $"{first.Outcome}: {first.Error}");
        Assert.False(first.Info.FromCache);
        Assert.Contains(first.Value.Category, new[] { EditCategory.FalseFactualClaim, EditCategory.PovPush });
        Assert.False(string.IsNullOrWhiteSpace(first.Value.ClaimEn));
        Assert.NotEmpty(first.Value.Evidence);
        Assert.InRange(first.Value.Confidence, 1, 5);
        Assert.True(first.Info.Usage.InputTokens > 0);

        var second = await Model.RunAsync<EditClassification>(request);

        Assert.True(second.IsOk);
        Assert.True(second.Info.FromCache);
        Assert.Equal(0, second.Info.Attempts);
        Assert.Equal(first.Value.ClaimEn, second.Value.ClaimEn);
    }

    [GeminiFact]
    public async Task Api_error_is_an_outcome_not_an_exception()
    {
        var result = await Model.RunAsync<EditClassification>(
            MainilaEdit("live-test-bad-model") with { Model = "gemini-no-such-model", Cache = CachePolicy.Bypass });

        Assert.Equal(AnalysisOutcome.Failed, result.Outcome);
        Assert.Null(result.Value);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}

/// <summary>Live Gemini test — skipped unless <c>GEMINI_TESTS=1</c> so <c>verify.sh</c> never spends money or needs network.</summary>
public sealed class GeminiFactAttribute : FactAttribute
{
    public GeminiFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("GEMINI_TESTS") != "1")
            Skip = "Live Gemini test — set GEMINI_TESTS=1 (and Gemini:ApiKey in user-secrets or Gemini__ApiKey) to enable.";
    }
}
