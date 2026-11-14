namespace AmpersandOfTruth.Tests.Features.Evidence;

using System.Globalization;
using AmpersandOfTruth.Web.Features.Evidence;
using Xunit;

public sealed class EvidenceTextTests
{
    [Fact]
    public void EvidenceTiers_strings_have_zero_violations_including_jargon()
    {
        foreach (var tier in EvidenceTiers.All)
        {
            Assert.Empty(CopyRules.FindViolations(tier.Label, includeJargon: true));
            Assert.Empty(CopyRules.FindViolations(tier.ShortLabel, includeJargon: true));
            Assert.Empty(CopyRules.FindViolations(tier.Meaning, includeJargon: true));
        }
    }

    [Fact]
    public void Attribution_constants_have_zero_violations_including_jargon()
    {
        Assert.Empty(CopyRules.FindViolations(Attribution.License, includeJargon: true));
        Assert.Empty(CopyRules.FindViolations(Attribution.FooterText, includeJargon: true));
        Assert.Empty(CopyRules.FindViolations(Attribution.FooterLinkText, includeJargon: true));
        Assert.Empty(CopyRules.FindViolations(Attribution.MethodologyRoute, includeJargon: true));
        Assert.Empty(CopyRules.FindViolations(Attribution.AutomatedAssessmentLabel, includeJargon: true));
        Assert.Empty(CopyRules.FindViolations(Attribution.MachineTranslationNotice, includeJargon: true));
    }

    [Fact]
    public void Methodology_texts_have_zero_violations_including_jargon()
    {
        Assert.Empty(CopyRules.FindViolations(Methodology.Summary, includeJargon: true));

        Assert.NotEmpty(Methodology.Limitations);
        foreach (var limitation in Methodology.Limitations)
        {
            Assert.Empty(CopyRules.FindViolations(limitation, includeJargon: true));
        }

        Assert.Empty(CopyRules.FindViolations(Methodology.AccountsCaveat, includeJargon: true));
    }

    [Fact]
    public void Methodology_limitations_count_is_five()
    {
        Assert.Equal(5, Methodology.Limitations.Count);
    }

    [Fact]
    public void ForRevision_produces_exact_expected_output()
    {
        var timestamp = DateTimeOffset.Parse("2014-03-02T20:20:00+02:00", CultureInfo.InvariantCulture);
        var formatted = Attribution.ForRevision(123456, timestamp, "85.76.x.x (anonymous)");

        const string expected = "fi.wikipedia.org revision 123456, 2014-03-02 18:20 UTC, by 85.76.x.x (anonymous). CC BY-SA 4.0.";
        Assert.Equal(expected, formatted);
    }

    [Fact]
    public void Attribution_license_url_is_expected_creative_commons_uri()
    {
        Assert.Equal(new Uri("https://creativecommons.org/licenses/by-sa/4.0/"), Attribution.LicenseUrl);
    }
}
