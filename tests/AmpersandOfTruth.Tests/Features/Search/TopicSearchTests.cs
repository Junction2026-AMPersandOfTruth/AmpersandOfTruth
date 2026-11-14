using AmpersandOfTruth.Web.Components.Layout;
using AmpersandOfTruth.Web.Features.Search;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace AmpersandOfTruth.Tests.Features.Search;

public sealed class TopicSearchTests : BunitContext
{
    public TopicSearchTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSearchFeature();
    }

    [Fact]
    public void Submitting_trimmed_topic_navigates_to_slug_and_records_recent_topic()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<TopicSearch>();

        cut.Find("[data-testid=topic-input]").Change("  Winter War ");
        cut.Find("form").Submit();

        Assert.Equal("http://localhost/investigate/winter-war", nav.Uri);

        var setItem = Assert.Single(JSInterop.Invocations["localStorage.setItem"]);
        Assert.Equal(RecentTopics.StorageKey, setItem.Arguments[0]);
        Assert.Contains("""{"label":"Winter War","slug":"winter-war"}""", setItem.Arguments[1]?.ToString());
    }

    [Fact]
    public void Filters_append_from_to_and_minimum_level_tiers_to_query_string()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<TopicSearch>();

        var details = cut.Find("[data-testid=filters-toggle]");
        Assert.False(details.HasAttribute("open"));

        cut.Find("[data-testid=filter-from]").Change("2014");
        cut.Find("[data-testid=filter-to]").Change("2016");
        cut.Find("[data-testid=filter-min-level-2]").Click();

        cut.Find("[data-testid=topic-input]").Change("Winter War");
        cut.Find("form").Submit();

        Assert.Equal("http://localhost/investigate/winter-war?from=2014&to=2016&tier=2,3", nav.Uri);
    }

    [Fact]
    public void Empty_submit_does_nothing_and_121_chars_shows_inline_validation_without_navigating()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<TopicSearch>();

        // Empty / whitespace submit: no error, no navigation
        cut.Find("[data-testid=topic-input]").Change("   ");
        cut.Find("form").Submit();

        Assert.Equal("http://localhost/", nav.Uri);
        Assert.Empty(cut.FindAll(".validation-message"));

        // 121 chars: inline validation message, no navigation
        cut.Find("[data-testid=topic-input]").Change(new string('a', 121));
        cut.Find("form").Submit();

        Assert.Equal("http://localhost/", nav.Uri);
        var msg = Assert.Single(cut.FindAll(".validation-message"));
        Assert.Contains("120", msg.TextContent);
    }

    [Fact]
    public void Chips_render_from_options_with_hrefs_to_their_slugs()
    {
        Services.Configure<SearchOptions>(o =>
        {
            o.SuggestedTopics =
            [
                new("Civil War 1918", "Civil War 1918"),
                new("Winter War", "Winter War"),
                new("Continuation War", "Continuation War"),
            ];
        });

        var cut = Render<TopicSearch>();

        var chips = cut.FindAll("[data-testid=topic-chip]");
        Assert.Equal(3, chips.Count);
        Assert.Equal("/investigate/civil-war-1918", chips[0].GetAttribute("href"));
        Assert.Equal("Civil War 1918", chips[0].TextContent.Trim());
        Assert.Equal("/investigate/winter-war", chips[1].GetAttribute("href"));
        Assert.Equal("/investigate/continuation-war", chips[2].GetAttribute("href"));
    }

    [Fact]
    public void Compact_variant_renders_in_MainLayout_slot_on_non_landing_routes_and_hides_on_root()
    {
        var nav = Services.GetRequiredService<NavigationManager>();

        // On '/', appbar-search-slot is empty
        var cut = Render<MainLayout>(p => p.Add(
            x => x.Body,
            b => b.AddMarkupContent(0, "<p>Body</p>")));
        Assert.Empty(cut.Find("[data-testid=appbar-search-slot]").Children);

        // Navigate away from '/': compact TopicSearch appears inside appbar-search-slot with aot-search--compact
        nav.NavigateTo("/methodology");
        cut.WaitForAssertion(() =>
        {
            var slot = cut.Find("[data-testid=appbar-search-slot]");
            var searchBox = slot.QuerySelector(".aot-search");
            Assert.NotNull(searchBox);
            Assert.Contains("aot-search--compact", searchBox.ClassList);

            var input = slot.QuerySelector("[data-testid=topic-input]");
            Assert.NotNull(input);
            Assert.Equal("Search a topic", input.GetAttribute("placeholder"));
            Assert.NotNull(slot.QuerySelector("[data-testid=topic-submit]"));
            Assert.Null(slot.QuerySelector("[data-testid=topic-chip]"));
            Assert.Null(slot.QuerySelector("[data-testid=filters-toggle]"));
        });

        // Navigating back to '/' hides the compact search again
        nav.NavigateTo("/");
        cut.WaitForAssertion(() =>
            Assert.Empty(cut.Find("[data-testid=appbar-search-slot]").Children));
    }

    [Fact]
    public void Compact_variant_with_Initial_value_navigates_even_when_out_of_scope()
    {
        Services.AddSingleton<IScopeCheck>(new FakeScopeCheck(false, ["Winter War"]));
        var nav = Services.GetRequiredService<NavigationManager>();

        var cut = Render<TopicSearch>(p => p
            .Add(x => x.Compact, true)
            .Add(x => x.Initial, "Mannerheim"));

        Assert.Equal("Mannerheim", cut.Find("[data-testid=topic-input]").GetAttribute("value"));

        cut.Find("form").Submit();
        Assert.Equal("http://localhost/investigate/mannerheim", nav.Uri);
    }

    [Fact]
    public void Typing_without_blur_submits_on_Enter_without_oninput_binding_and_Disabled_blocks_submit()
    {
        var module = JSInterop.SetupModule("./Features/Search/TopicSearch.razor.js");
        module.Setup<string?>("readInputValue", _ => true).SetResult("Mannerheim");

        var nav = Services.GetRequiredService<NavigationManager>();
        var cut = Render<TopicSearch>();

        var input = cut.Find("[data-testid=topic-input]");
        Assert.False(input.HasAttribute("oninput"));
        Assert.False(input.HasAttribute("blazor:oninput"));

        // Even in browsers (Firefox/Safari/mobile keyboards) that fire form submit on Enter before onchange blurs the input,
        // HandleSubmit reads the live input value once on submit — zero per-keystroke round-trips.
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
            Assert.Equal("http://localhost/investigate/mannerheim", nav.Uri));

        // Disabled mode disables both input and button and ignores form submit
        nav.NavigateTo("/");
        var disabledCut = Render<TopicSearch>(p => p.Add(x => x.Disabled, true));
        Assert.True(disabledCut.Find("[data-testid=topic-input]").HasAttribute("disabled"));
        Assert.True(disabledCut.Find("[data-testid=topic-submit]").HasAttribute("disabled"));

        disabledCut.Find("[data-testid=topic-input]").Change("Winter War");
        disabledCut.Find("form").Submit();
        Assert.Equal("http://localhost/", nav.Uri);
    }

    private sealed class FakeScopeCheck(bool inScope, IReadOnlyList<string> suggestions) : IScopeCheck
    {
        public Task<ScopeResult> CheckAsync(string topic, CancellationToken ct = default) =>
            Task.FromResult(new ScopeResult(inScope, suggestions));
    }
}
