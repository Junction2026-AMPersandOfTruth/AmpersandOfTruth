using AmpersandOfTruth.Tests.Features.Investigations.Incident;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.WebSpread;
using AmpersandOfTruth.Web.Features.WebSpread.Dating;
using AmpersandOfTruth.Web.Features.WebSpread.Matching;
using AmpersandOfTruth.Web.Features.WebSpread.Planning;
using AmpersandOfTruth.Web.Features.WebSpread.Spread;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.WebSpread;

public sealed class WebSpreadPanelTests : BunitContext
{
    private readonly WebSpreadServiceKit _kit = new();
    private readonly IncidentView _incident = FakeIncidentSource.BuildIncident(id: "inc-1");

    protected override void Dispose(bool disposing)
    {
        if (disposing) _kit.Dispose();
        base.Dispose(disposing);
    }

    private IRenderedComponent<WebSpreadPanel> RenderPanel(bool registerService = true)
    {
        if (registerService) Services.AddSingleton(_kit.Service());
        return Render<WebSpreadPanel>(p => p.Add(x => x.Incident, _incident));
    }

    private async Task StoreAsync(WebSpreadResult result) => Assert.True(await _kit.Results.SaveAsync(result));

    private static WebSpreadSource Source(string url, Stance stance, SpreadBucket bucket, DateOnly? date,
        DateConfidence confidence = DateConfidence.High, bool fromWikipedia = false) => new(
        url, new Uri(url).Host, "Title of " + new Uri(url).Host, SourceType.News, stance, MatchKind.SameClaimDifferentWording,
        "a quoted passage from the page", false, bucket, false, fromWikipedia,
        new PageDating(date, null, null, date is null ? null : DateMethod.MetaPublished, confidence, [], []), [QueryKind.Keywords]);

    [Fact]
    public void Renders_nothing_when_the_feature_is_not_registered()
    {
        var cut = RenderPanel(registerService: false);
        Assert.Empty(cut.FindAll("[data-testid=webspread-panel]"));
    }

    [Fact]
    public void Renders_nothing_without_credentials_and_without_a_stored_result()
    {
        _kit.Oxylabs.ApiKey = null;
        var cut = RenderPanel();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=webspread-panel]")));
    }

    [Fact]
    public void Safe_mode_without_a_stored_result_shows_nothing()
    {
        _kit.Demo.SafeMode = true;
        var cut = RenderPanel();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=webspread-panel]")));
    }

    [Fact]
    public async Task Safe_mode_shows_a_stored_result_grouped_by_timing_with_no_live_actions()
    {
        _kit.Demo.SafeMode = true;
        await StoreAsync(WebSpreadResultsTests.Result(WebSpreadStatus.Completed, 5,
        [
            Source("https://old.example/a", Stance.Asserts, SpreadBucket.Before, new DateOnly(2010, 1, 2)),
            Source("https://copy.example/b", Stance.Asserts, SpreadBucket.AfterRemoval, new DateOnly(2016, 5, 1), DateConfidence.Low, fromWikipedia: true),
            Source("https://nodate.example/c", Stance.RepeatsWithAttribution, SpreadBucket.Undated, null)
                with { Quote = "## Valkoiset [voittavat](/wiki/X \"X\") **sodan**" },
            Source("https://check.example/d", Stance.Refutes, SpreadBucket.During, new DateOnly(2014, 3, 10)),
        ]) with { Headline = "Found on 1 site before the Wikipedia edit, 1 site after it and 1 site without a reliable date." });

        var cut = RenderPanel();

        cut.WaitForElement("[data-testid=webspread-headline]");
        Assert.StartsWith("Found on 1 site before", cut.Find("[data-testid=webspread-headline]").TextContent);
        Assert.Single(cut.FindAll("[data-testid=webspread-bucket-before] [data-testid=webspread-source]"));
        Assert.Single(cut.FindAll("[data-testid=webspread-bucket-after] [data-testid=webspread-source]"));
        Assert.Single(cut.FindAll("[data-testid=webspread-bucket-undated] [data-testid=webspread-source]"));
        Assert.Empty(cut.FindAll("[data-testid=webspread-bucket-during]")); // the only "during" page argues against it
        Assert.Single(cut.FindAll("[data-testid=webspread-refuting] [data-testid=webspread-source]"));
        Assert.Single(cut.FindAll("[data-testid=webspread-date-approx]"));
        Assert.Single(cut.FindAll("[data-testid=webspread-from-wikipedia]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=webspread-causation]"));
        Assert.Contains("2 Jan 2010", cut.Markup);
        Assert.Contains("Valkoiset voittavat sodan", cut.Find("[data-testid=webspread-bucket-undated] q").TextContent); // Markdown stripped

        Assert.Empty(cut.FindAll("[data-testid=webspread-start]"));
        Assert.Empty(cut.FindAll("[data-testid=webspread-rerun]"));
    }

    [Fact]
    public async Task Links_out_open_safely_and_page_text_is_not_rendered_as_markup()
    {
        var evil = Source("https://evil.example/x", Stance.Asserts, SpreadBucket.Before, new DateOnly(2010, 1, 2))
            with { Title = "<b>bold</b><script>alert(1)</script>", Quote = "<img src=x onerror=alert(1)>" };
        await StoreAsync(WebSpreadResultsTests.Result(WebSpreadStatus.Completed, 2, [evil]));

        var cut = RenderPanel();

        var link = cut.WaitForElement("[data-testid=webspread-source] a");
        Assert.Equal("noopener noreferrer nofollow", link.GetAttribute("rel"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Empty(cut.FindAll("[data-testid=webspread-panel] script"));
        Assert.Empty(cut.FindAll("[data-testid=webspread-panel] img"));
        Assert.Contains("&lt;script&gt;", cut.Markup);
    }

    [Fact]
    public async Task A_stored_empty_result_says_nothing_was_found_and_what_was_searched()
    {
        await StoreAsync(WebSpreadResultsTests.Result(WebSpreadStatus.Completed, 2));

        var cut = RenderPanel();

        cut.WaitForElement("[data-testid=empty-state]");
        Assert.Contains("Finnish", cut.Find("[data-testid=empty-state]").TextContent);
        Assert.NotEmpty(cut.FindAll("[data-testid=webspread-searched] code"));
        Assert.NotEmpty(cut.FindAll("[data-testid=webspread-rerun]")); // live: can search again
    }

    [Fact]
    public void Tracing_streams_to_a_result_that_is_stored_for_next_time()
    {
        _kit.WithOneCopy();
        var cut = RenderPanel();

        cut.WaitForElement("[data-testid=webspread-start]").Click();

        cut.WaitForElement("[data-testid=webspread-headline]", TimeSpan.FromSeconds(10));
        Assert.Single(cut.FindAll("[data-testid=webspread-source]"));
        Assert.Empty(cut.FindAll("[data-testid=webspread-progress]"));
        Assert.NotNull(_kit.Results.GetAsync("inc-1").GetAwaiter().GetResult());
    }

    [Fact]
    public void A_failed_trace_shows_an_error_with_a_retry_and_stores_nothing()
    {
        _kit.WithOneCopy();
        _kit.Search.Outcome = Web.Features.WebSpread.WebCallOutcome.Failed;
        var cut = RenderPanel();

        cut.WaitForElement("[data-testid=webspread-start]").Click();

        cut.WaitForElement("[data-testid=webspread-failed]", TimeSpan.FromSeconds(10));
        Assert.NotEmpty(cut.FindAll("[data-testid=webspread-retry]"));
        Assert.Empty(cut.FindAll("[data-testid=webspread-headline]"));
        Assert.Null(_kit.Results.GetAsync("inc-1").GetAwaiter().GetResult());
    }
}
