using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Investigations;
using AmpersandOfTruth.Web.Features.Library;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace AmpersandOfTruth.Tests.Features.Library;

public sealed class LibraryPageTests : BunitContext
{
    private readonly TestDb _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 15, 0, 0, TimeSpan.Zero));
    private readonly LibraryStore _store;

    public LibraryPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _db.AddTo(Services);
        Services.AddSingleton<TimeProvider>(_clock);
        Services.AddLibraryFeature();
        _store = Services.GetRequiredService<LibraryStore>();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _db.Dispose();
        }
    }

    [Fact]
    public void Empty_library_renders_empty_state_with_link_home()
    {
        var cut = Render<LibraryPage>();

        cut.WaitForAssertion(() =>
        {
            var empty = cut.Find("[data-testid=library-empty]");
            Assert.Contains("No saved investigations yet", empty.TextContent);
            var link = empty.QuerySelector("a[href='/']");
            Assert.NotNull(link);
        });
    }

    [Fact]
    public async Task Lists_saved_items_with_tier_counts_supports_rename_and_requires_confirm_before_delete()
    {
        var owner = Services.GetRequiredService<OwnerKey>();
        owner.Seed("owner-test");

        var snapshot = new SavedSnapshot(
            "Winter War",
            [new TopicArticle("fi", 1150, "Talvisota", 0.04)],
            [
                MakeIncident("inc-1", EvidenceTier.Campaign, 95),
                MakeIncident("inc-2", EvidenceTier.Campaign, 88),
                MakeIncident("inc-3", EvidenceTier.Contested, 50),
            ],
            _clock.GetUtcNow());

        var id = await _store.SaveAsync("owner-test", "Winter War dossier", snapshot);

        var cut = Render<LibraryPage>();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid=library-item]"));
            Assert.Contains("Winter War dossier", row.TextContent);
            Assert.Contains("Winter War", row.TextContent);
            Assert.Contains("14 Nov 2026", row.TextContent);

            var badges = row.QuerySelectorAll("[data-testid=tier-badge]");
            Assert.Equal(3, badges.Length);

            var nums = row.QuerySelectorAll("td:nth-child(3) .aot-num").Select(n => n.TextContent.Trim()).ToArray();
            Assert.Equal(["2", "1", "0"], nums);

            var openLink = row.QuerySelector("[data-testid=library-open]");
            Assert.NotNull(openLink);
            Assert.Equal($"/investigate/winter-war?saved={id}", openLink.GetAttribute("href"));
        });

        // Inline rename
        cut.Find("[data-testid=library-rename]").Click();
        cut.WaitForAssertion(() =>
            Assert.Equal("Winter War dossier", cut.Find("[data-testid=saved-title-input]").GetAttribute("value")));
        cut.Find("[data-testid=saved-title-input]").Input("Renamed Winter War");
        cut.WaitForAssertion(() =>
            Assert.Equal("Renamed Winter War", cut.Find("[data-testid=saved-title-input]").GetAttribute("value")));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid=library-item]"));
            Assert.Contains("Renamed Winter War", row.TextContent);
        });

        // Delete requires confirm step
        cut.Find("[data-testid=library-delete]").Click();
        Assert.Single(cut.FindAll("[data-testid=library-item]"));
        Assert.NotNull(await _store.GetAsync(id));

        var confirmBtn = cut.WaitForElement("[data-testid=library-delete-confirm]");
        confirmBtn.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Empty(cut.FindAll("[data-testid=library-item]"));
            Assert.NotNull(cut.Find("[data-testid=library-empty]"));
        });
        Assert.Null(await _store.GetAsync(id));

        // Rendered page copy passes CopyRules (including jargon)
        var doc = new HtmlParser().ParseDocument(cut.Markup);
        var text = string.Join(" ", doc.Descendants<IText>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)));
        Assert.Empty(CopyRules.FindViolations(text, includeJargon: true));
    }

    private static IncidentRecord MakeIncident(string id, EvidenceTier tier, double score) =>
        new()
        {
            Id = id,
            PageId = 1150,
            InsertingRevIds = [1001],
            RemovingRevIds = [1002],
            Tier = tier,
            Score = score,
            ClaimEn = $"Claim {id}",
        };
}
