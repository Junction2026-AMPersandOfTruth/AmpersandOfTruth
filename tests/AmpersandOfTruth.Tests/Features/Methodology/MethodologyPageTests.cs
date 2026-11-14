using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AmpersandOfTruth.Web.Features.Evidence;
using AmpersandOfTruth.Web.Features.Methodology;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

// Namespace avoids `.Methodology` / `.MethodologyPage` segments that would shadow types of the same name.
namespace AmpersandOfTruth.Tests.Features.MethodologyFeature;

public sealed class MethodologyPageTests : BunitContext
{
    [Fact]
    public void Renders_all_six_sections_in_order_with_matching_h2_ids_and_page_title()
    {
        JSInterop.Setup<string>("Blazor._internal.PageTitle.getAndRemoveExistingTitle").SetResult("");
        var head = Render<HeadOutlet>();
        var cut = Render<MethodologyPage>();

        Assert.Equal("How this works · Ampersand of Truth", head.Find("title").TextContent);

        var sections = cut.FindAll("[data-testid=methodology-section]");
        var expectedIds = new[] { "how", "levels", "quality", "coverage", "limitations", "attribution" };

        Assert.Equal(expectedIds, sections.Select(s => s.GetAttribute("data-section")).ToArray());

        for (var i = 0; i < expectedIds.Length; i++)
        {
            var id = expectedIds[i];
            var heading = sections[i].QuerySelector("h2");
            Assert.NotNull(heading);
            Assert.Equal(id, heading.GetAttribute("id"));
        }
    }

    [Fact]
    public void How_section_renders_summary_and_five_labelled_pipeline_boxes_with_captions_and_arrows()
    {
        var cut = Render<MethodologyPage>();

        var how = cut.Find("[data-testid=methodology-section][data-section=how]");
        Assert.Contains(Methodology.Summary, how.TextContent);

        var diagram = how.QuerySelector("[data-testid=pipeline-diagram]");
        Assert.NotNull(diagram);
        Assert.Equal("img", diagram.GetAttribute("role"));
        Assert.False(string.IsNullOrWhiteSpace(diagram.GetAttribute("aria-label")));

        var expectedTitles = new[] { "Topic", "Articles", "Edit history", "Signals", "Assessment" };

        var boxes = diagram.QuerySelectorAll("[data-testid=pipeline-box]");
        Assert.Equal(5, boxes.Length);
        Assert.Equal(
            expectedTitles,
            boxes.Select(b => b.QuerySelector(".pipeline-box__title")?.TextContent).ToArray());
        Assert.All(boxes, box =>
        {
            Assert.NotNull(box.QuerySelector("rect"));
            var caption = box.QuerySelector(".pipeline-box__caption")?.TextContent;
            Assert.False(string.IsNullOrWhiteSpace(caption));
        });

        Assert.Equal(4, diagram.QuerySelectorAll("[data-testid=pipeline-arrow]").Length);

        var narrowBoxes = diagram.QuerySelectorAll("[data-testid=pipeline-svg-narrow] .pipeline-box-narrow");
        Assert.Equal(5, narrowBoxes.Length);
        Assert.Equal(
            expectedTitles,
            narrowBoxes.Select(b => b.QuerySelector(".pipeline-box__title")?.TextContent).ToArray());
        Assert.Equal(4, diagram.QuerySelectorAll("[data-testid=pipeline-svg-narrow] .pipeline-arrow-narrow").Length);
    }

    [Fact]
    public void Levels_section_renders_three_TierBadges_for_levels_1_to_3_with_meanings_and_requirements()
    {
        var cut = Render<MethodologyPage>();

        var levelsSection = cut.Find("[data-testid=methodology-section][data-section=levels]");
        var cards = levelsSection.QuerySelectorAll("[data-testid=methodology-level]");
        Assert.Equal(3, cards.Length);

        var badges = levelsSection.QuerySelectorAll("[data-testid=tier-badge]");
        Assert.Equal(3, badges.Length);

        for (var i = 0; i < EvidenceTiers.All.Count; i++)
        {
            var expected = EvidenceTiers.All[i];
            var card = cards[i];
            var badge = badges[i];

            Assert.Equal(expected.Level.ToString(), card.GetAttribute("data-level"));
            Assert.Contains($"Level {expected.Level}", card.TextContent);
            Assert.Equal(expected.Id, badge.GetAttribute("data-tier"));
            Assert.Equal(expected.Label, badge.TextContent);
            Assert.Equal(expected.Level, badge.QuerySelectorAll(".aot-tier__pips i.on").Length);

            Assert.Contains(expected.Meaning, card.TextContent);
            Assert.Contains("What it takes", card.TextContent);
            Assert.NotEmpty(card.QuerySelectorAll("ul li"));
        }
    }

    [Fact]
    public void Quality_coverage_limitations_and_attribution_sections_match_contracts()
    {
        var cut = Render<MethodologyPage>();

        var quality = cut.Find("[data-testid=methodology-section][data-section=quality]");
        Assert.Contains("Being measured", quality.TextContent);
        Assert.Contains("Precision", quality.TextContent, StringComparison.OrdinalIgnoreCase);
        var qualityBodyText = string.Join(" ", quality.QuerySelectorAll("p, li, dd").Select(e => e.TextContent));
        Assert.DoesNotMatch(new Regex(@"\d"), qualityBodyText);

        var coverage = cut.Find("[data-testid=methodology-section][data-section=coverage]");
        Assert.Contains("19,742", coverage.TextContent);
        Assert.Contains("History of Finland", coverage.TextContent);
        Assert.Contains("2003", coverage.TextContent);
        Assert.Contains("1 Oct 2026", coverage.TextContent);
        Assert.Contains("Finnish only", coverage.TextContent);

        var limitations = cut.Find("[data-testid=methodology-section][data-section=limitations]");
        var items = limitations.QuerySelectorAll("[data-testid=methodology-limitation]");
        Assert.Equal(Methodology.Limitations.Count, items.Length);
        foreach (var sentence in Methodology.Limitations)
        {
            Assert.Contains(sentence, limitations.TextContent);
        }

        var attribution = cut.Find("[data-testid=methodology-section][data-section=attribution]");
        Assert.Contains(Attribution.FooterText, attribution.TextContent);
        Assert.Contains("Wikimedia dumps", attribution.TextContent);
        Assert.Contains("FineWiki", attribution.TextContent);
        Assert.Contains("MariaDB Foundation", attribution.TextContent);

        var licenseLink = attribution.QuerySelector("[data-testid=license-link]");
        Assert.NotNull(licenseLink);
        Assert.Equal(Attribution.LicenseUrl.AbsoluteUri, licenseLink.GetAttribute("href"));
        Assert.Contains(Attribution.License, licenseLink.TextContent);
    }

    [Fact]
    public void Rendered_page_text_and_accessible_labels_pass_CopyRules_including_jargon()
    {
        var cut = Render<MethodologyPage>();

        var doc = new HtmlParser().ParseDocument(cut.Markup);
        var textSegments = doc.Descendants<IText>()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        var renderedText = string.Join(" ", textSegments);

        var textViolations = CopyRules.FindViolations(renderedText, includeJargon: true);
        Assert.Empty(textViolations);

        foreach (var element in cut.FindAll("[title], [aria-label]"))
        {
            var title = element.GetAttribute("title");
            if (!string.IsNullOrWhiteSpace(title))
            {
                Assert.Empty(CopyRules.FindViolations(title, includeJargon: true));
            }

            var ariaLabel = element.GetAttribute("aria-label");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
            {
                Assert.Empty(CopyRules.FindViolations(ariaLabel, includeJargon: true));
            }
        }
    }

    [Fact]
    public void PipelineDiagram_uses_design_tokens_without_hardcoded_hex_colours()
    {
        var cut = Render<PipelineDiagram>();
        Assert.DoesNotMatch(new Regex(@"#[0-9a-fA-F]{3,8}\b"), cut.Markup);
    }
}
