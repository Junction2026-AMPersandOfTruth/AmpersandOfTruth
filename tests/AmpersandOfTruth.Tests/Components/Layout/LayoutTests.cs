using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Components.Layout;
using AmpersandOfTruth.Web.Components.Pages;
using AmpersandOfTruth.Web.Features.Evidence;
using Bunit;

namespace AmpersandOfTruth.Tests.Components.Layout;

public sealed class LayoutTests : BunitContext
{
    [Fact]
    public void AppCss_starts_with_tokens_and_contains_every_aot_css_line_plus_blazor_rules()
    {
        var repoRoot = FindRepoRoot();
        var lf = ((char)10).ToString();
        var tokensLines = File.ReadAllLines(Path.Combine(repoRoot, "docs", "design", "tokens.css"));
        var aotLines = File.ReadAllLines(Path.Combine(repoRoot, "docs", "design", "aot.css"));
        var appLines = File.ReadAllLines(Path.Combine(repoRoot, "src", "AmpersandOfTruth.Web", "wwwroot", "app.css"));

        var tokensCss = string.Join(lf, tokensLines);
        var aotCss = string.Join(lf, aotLines);
        var appCss = string.Join(lf, appLines);

        const string header = "/* generated from docs/design/tokens.css + aot.css — edit those, then re-copy */";
        Assert.Equal(header, appLines[0]);
        Assert.StartsWith(header + lf + tokensCss, appCss, StringComparison.Ordinal);

        var appLineSet = appLines.ToHashSet(StringComparer.Ordinal);
        var nonEmptyAotLines = aotLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        Assert.NotEmpty(nonEmptyAotLines);
        foreach (var line in nonEmptyAotLines)
        {
            Assert.Contains(line, appLineSet);
        }

        Assert.Contains(aotCss, appCss, StringComparison.Ordinal);

        Assert.Contains("#blazor-error-ui", appCss, StringComparison.Ordinal);
        Assert.Contains(".valid.modified", appCss, StringComparison.Ordinal);
        Assert.Contains(".invalid", appCss, StringComparison.Ordinal);
        Assert.Contains(".validation-message", appCss, StringComparison.Ordinal);
        Assert.Contains("h1:focus", appCss, StringComparison.Ordinal);
        Assert.Contains(".blazor-error-boundary", appCss, StringComparison.Ordinal);
    }

    [Fact]
    public void MainLayout_renders_wordmark_nav_links_search_slot_and_attribution_footer()
    {
        var cut = Render<MainLayout>(parameters => parameters.Add(
            p => p.Body,
            builder => builder.AddMarkupContent(0, """<p data-testid="body-probe">Page body</p>""")));

        var brand = cut.Find("header.aot-appbar a.aot-brand");
        Assert.Equal("/", brand.GetAttribute("href"));
        Assert.Equal("&", brand.QuerySelector("em")?.TextContent);
        Assert.Contains("Ampersand of Truth", brand.TextContent);

        var searchSlot = cut.Find("[data-testid=appbar-search-slot]");
        Assert.Contains("aot-appbar__search", searchSlot.ClassList);
        Assert.Empty(searchSlot.Children);

        var navLinks = cut.FindAll("header.aot-appbar nav a");
        Assert.Equal(2, navLinks.Count);
        Assert.Equal("/", navLinks[0].GetAttribute("href"));
        Assert.Equal("Investigate", navLinks[0].TextContent.Trim());
        Assert.Equal("/methodology", navLinks[1].GetAttribute("href"));
        Assert.Equal("Methodology", navLinks[1].TextContent.Trim());

        Assert.Equal("Page body", cut.Find("main.aot-page [data-testid=body-probe]").TextContent);

        var footer = cut.Find("[data-testid=site-footer]");
        Assert.Contains("aot-footer", footer.ClassList);
        Assert.Contains(Attribution.FooterText, footer.TextContent);

        var methodologyLink = Assert.Single(
            footer.QuerySelectorAll("a"),
            a => a.GetAttribute("href") == Attribution.MethodologyRoute);
        Assert.Equal(Attribution.FooterLinkText, methodologyLink.TextContent.Trim());

        var analysisLink = Assert.Single(
            footer.QuerySelectorAll("a"),
            a => a.GetAttribute("href") == "/analysis");
        Assert.Equal("Assessment usage", analysisLink.TextContent.Trim());

        var debugLink = Assert.Single(
            footer.QuerySelectorAll("a"),
            a => a.GetAttribute("href") == "/investigate");
        Assert.Equal("Pipeline debug", debugLink.TextContent.Trim());
    }

    [Fact]
    public void Home_renders_display_heading_placeholder()
    {
        var cut = Render<Home>();

        var heading = cut.Find("h1.t-display");
        Assert.Equal("What was pushed into Wikipedia?", heading.TextContent.Trim());
    }

    [Fact]
    public void ReconnectModal_uses_calm_copy_and_preserves_required_js_ids()
    {
        var cut = Render<ReconnectModal>();

        var dialog = cut.Find("dialog#components-reconnect-modal");
        Assert.Contains("Reconnecting to the investigation…", dialog.TextContent);
        Assert.NotNull(cut.Find("#components-seconds-to-next-attempt"));
        Assert.NotNull(cut.Find("button#components-reconnect-button"));
        Assert.NotNull(cut.Find("button#components-resume-button"));
    }

    [Fact]
    public void Every_routable_page_has_a_PageTitle_and_shell_assets_are_present()
    {
        var repoRoot = FindRepoRoot();
        var webDir = Path.Combine(repoRoot, "src", "AmpersandOfTruth.Web");

        var pageFiles = Directory.EnumerateFiles(webDir, "*.razor", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"^\s*@page\s+""", RegexOptions.Multiline))
            .ToList();

        Assert.NotEmpty(pageFiles);
        foreach (var pageFile in pageFiles)
        {
            var content = File.ReadAllText(pageFile);
            var rel = Path.GetRelativePath(repoRoot, pageFile);
            Assert.True(
                Regex.IsMatch(content, @"<PageTitle>[^<]+</PageTitle>"),
                $"{rel} is missing a non-empty <PageTitle>.");
        }

        var appRazor = File.ReadAllText(Path.Combine(webDir, "Components", "App.razor"));
        Assert.Contains(
            """<meta name="description" content="See what was pushed into Wikipedia, and what the community took out." />""",
            appRazor,
            StringComparison.Ordinal);
        Assert.Contains("""href="favicon.svg" """, appRazor, StringComparison.Ordinal);
        Assert.Contains("""href="favicon.png" """, appRazor, StringComparison.Ordinal);

        var faviconSvgPath = Path.Combine(webDir, "wwwroot", "favicon.svg");
        Assert.True(File.Exists(faviconSvgPath), "wwwroot/favicon.svg should exist.");
        var faviconSvg = File.ReadAllText(faviconSvgPath);
        Assert.Contains("#3366cc", faviconSvg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&amp;", faviconSvg, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root containing AmpersandOfTruth.sln");
    }
}
