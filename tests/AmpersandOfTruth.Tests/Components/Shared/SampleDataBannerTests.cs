using AmpersandOfTruth.Web.Components.Shared;
using AmpersandOfTruth.Web.Features.Investigations.View;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AmpersandOfTruth.Tests.Components.Shared;

public sealed class SampleDataBannerTests : BunitContext
{
    [Fact]
    public void Rendered_when_Source_is_Fixtures()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(Options.Create(new InvestigationSourceOptions
        {
            Source = IncidentSourceKind.Fixtures,
        }));

        var cut = ctx.Render<SampleDataBanner>();

        var banner = cut.Find("[data-testid=sample-data-banner]");
        Assert.Contains("aot-banner--warn", banner.ClassList);
        Assert.Equal("status", banner.GetAttribute("role"));
        Assert.Contains("Sample incidents: not findings", banner.TextContent);
        Assert.Contains(
            "These incidents are hand-written examples on real edits, used while the analysis is being built. Articles, edits and diffs are real.",
            banner.TextContent);
    }

    [Fact]
    public void Absent_when_Source_is_Pipeline()
    {
        using var ctx = new BunitContext();
        ctx.Services.AddSingleton(Options.Create(new InvestigationSourceOptions
        {
            Source = IncidentSourceKind.Pipeline,
        }));

        var cut = ctx.Render<SampleDataBanner>();

        Assert.Empty(cut.FindAll("[data-testid=sample-data-banner]"));
        Assert.Empty(cut.Markup.Trim());
    }
}
