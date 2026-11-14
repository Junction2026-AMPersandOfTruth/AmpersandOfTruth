using AmpersandOfTruth.Web.Components.Shared;
using Bunit;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class StatusBannerTests : BunitContext
{
    [Theory]
    [InlineData(StatusKind.Info, "aot-banner--info", "status")]
    [InlineData(StatusKind.Warn, "aot-banner--warn", "status")]
    [InlineData(StatusKind.Error, "aot-banner--error", "alert")]
    public void Kind_maps_to_expected_css_modifier_and_aria_role(StatusKind kind, string expectedClass, string expectedRole)
    {
        var cut = Render<StatusBanner>(p => p
            .Add(x => x.Kind, kind)
            .Add(x => x.Title, "Service status notice."));

        var banner = cut.Find("[data-testid=status-banner]");
        Assert.Contains("aot-banner", banner.ClassList);
        Assert.Contains(expectedClass, banner.ClassList);
        Assert.Equal(expectedRole, banner.GetAttribute("role"));
        Assert.Contains("Service status notice.", banner.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=status-retry]"));
        Assert.Empty(cut.FindAll("details"));
    }

    [Fact]
    public void Detail_renders_inside_details_and_Retry_renders_and_fires_only_when_callback_is_set()
    {
        var retried = 0;

        var cut = Render<StatusBanner>(p => p
            .Add(x => x.Kind, StatusKind.Error)
            .Add(x => x.Title, "We cannot reach the Wikipedia data right now.")
            .Add(x => x.Detail, "MySqlException · stage=retrieval")
            .Add(x => x.TestId, "corpus-error")
            .Add(x => x.OnRetry, () => retried++)
            .AddChildContent("Topics analysed earlier still open."));

        var banner = cut.Find("[data-testid=corpus-error]");
        Assert.Contains("Topics analysed earlier still open.", banner.TextContent);

        var details = banner.QuerySelector("details");
        Assert.NotNull(details);
        Assert.Equal("Details", details.QuerySelector("summary")?.TextContent);
        Assert.Contains("MySqlException · stage=retrieval", details.TextContent);

        var retry = cut.Find("[data-testid=status-retry]");
        Assert.Equal("Retry", retry.TextContent);
        retry.Click();
        Assert.Equal(1, retried);
    }
}
