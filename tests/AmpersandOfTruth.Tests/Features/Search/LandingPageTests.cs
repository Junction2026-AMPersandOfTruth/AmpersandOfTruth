using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations.View;
using AmpersandOfTruth.Web.Features.Search;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AmpersandOfTruth.Tests.Features.Search;

public sealed class LandingPageTests : BunitContext
{
    public LandingPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddHealthChecks();
        Services.AddSearchFeature();
    }

    [Fact]
    public void Out_of_scope_topic_shows_out_of_scope_empty_state_with_suggestion_chips_and_stays_on_root()
    {
        Services.AddSingleton<IScopeCheck>(
            new FakeScopeCheck(false, ["Winter War", "Finlandization"]));

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<LandingPage>();

        cut.Find("[data-testid=topic-input]").Change("Roman Empire");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var outOfScope = cut.Find("[data-testid=out-of-scope]");
            Assert.Contains("Our data covers the history of Finland. ‘Roman Empire’ is outside it.", outOfScope.TextContent);

            var chips = outOfScope.QuerySelectorAll("a.aot-chip");
            Assert.Equal(2, chips.Length);
            Assert.Equal("/investigate/winter-war", chips[0].GetAttribute("href"));
            Assert.Equal("Winter War", chips[0].TextContent.Trim());
            Assert.Equal("/investigate/finlandization", chips[1].GetAttribute("href"));
            Assert.Equal("Finlandization", chips[1].TextContent.Trim());
        });

        Assert.Equal("http://localhost/", nav.Uri);
    }

    [Fact]
    public void Recent_items_from_localStorage_render_on_landing_page()
    {
        JSInterop.Setup<string?>("localStorage.getItem", RecentTopics.StorageKey)
            .SetResult("""[{"label":"Winter War","slug":"winter-war"},{"label":"Ceded Karelia","slug":"ceded-karelia"}]""");

        var cut = Render<LandingPage>();

        cut.WaitForAssertion(() =>
        {
            var items = cut.FindAll("[data-testid=recent-item]");
            Assert.Equal(2, items.Count);
            Assert.Equal("Winter War", items[0].TextContent.Trim());
            Assert.Equal("/investigate/winter-war", items[0].GetAttribute("href"));
            Assert.Equal("Ceded Karelia", items[1].TextContent.Trim());
            Assert.Equal("/investigate/ceded-karelia", items[1].GetAttribute("href"));
        });
    }

    [Fact]
    public void Recent_when_js_throws_hides_section_without_exception()
    {
        JSInterop.Setup<string?>("localStorage.getItem", RecentTopics.StorageKey)
            .SetException(new InvalidOperationException("localStorage blocked"));

        var cut = Render<LandingPage>();

        Assert.Empty(cut.FindAll("[data-testid=recent-item]"));
        Assert.DoesNotContain("Recent in this browser", cut.Markup);
    }

    [Fact]
    public void Degraded_corpus_or_embedding_health_shows_warn_status_banner_with_detail()
    {
        Services.AddHealthChecks()
            .AddCheck("corpus", () => HealthCheckResult.Degraded("corpus 127.0.0.1:3307 unreachable"), ["corpus"])
            .AddCheck("embedding", () => HealthCheckResult.Healthy("bge-m3 ok"), ["embedding"]);

        var cut = Render<LandingPage>();

        cut.WaitForAssertion(() =>
        {
            var banner = cut.Find("[data-testid=status-banner]");
            Assert.Contains("aot-banner--warn", banner.ClassList);
            Assert.Contains("Search is limited right now", banner.TextContent);
            Assert.Contains("chips still open saved investigations", banner.TextContent);
            Assert.Contains("corpus 127.0.0.1:3307 unreachable", banner.TextContent);
        });
    }

    [Fact]
    public void Rendered_landing_copy_has_no_copy_or_jargon_violations_and_default_slugs_match_fixtures()
    {
        var cut = Render<LandingPage>();

        var visibleText = cut.Nodes.OfType<AngleSharp.Dom.IElement>()
            .Select(e => e.TextContent)
            .Aggregate("", (a, b) => a + " " + b);

        Assert.Empty(CopyRules.FindViolations(visibleText, includeJargon: true));

        var defaultSlugs = SearchOptions.DefaultSuggestedTopics.Select(t => TopicSlug.From(t.Topic)).ToList();
        Assert.Equal(
            ["civil-war-1918", "winter-war", "continuation-war", "mannerheim", "mainila-shots", "ceded-karelia", "finlandization"],
            defaultSlugs);
    }

    private sealed class FakeScopeCheck(bool inScope, IReadOnlyList<string> suggestions) : IScopeCheck
    {
        public Task<ScopeResult> CheckAsync(string topic, CancellationToken ct = default) =>
            Task.FromResult(new ScopeResult(inScope, suggestions));
    }
}
