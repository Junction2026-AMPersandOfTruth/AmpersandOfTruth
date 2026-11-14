using System.Globalization;
using System.Text;
using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Web.Features.Reports;

public static class MarkdownReport
{
    private static readonly string NewLineLf = new([(char)10]);

    public static string Render(InvestigationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder(4096);

        // 1. Header & coverage
        sb.Append("# ").AppendLine(NormalizeSingleLine(report.Title));
        sb.AppendLine();
        sb.Append("- **Topic:** ").AppendLine(NormalizeSingleLine(report.Topic));
        sb.Append("- **Generated ").Append(FormatUtc(report.GeneratedAt)).AppendLine("**");
        sb.Append("- **Analysed ").Append(FormatUtc(report.AnalysedAt)).AppendLine("**");
        sb.Append("- **Data coverage:** ").AppendLine(ReportBuilder.FormatCoverageLine(report.Coverage));
        sb.AppendLine();

        // 2. Executive summary
        sb.AppendLine("## Executive summary");
        sb.AppendLine();
        AppendTierCount(sb, report, EvidenceTier.Campaign);
        AppendTierCount(sb, report, EvidenceTier.Contested);
        AppendTierCount(sb, report, EvidenceTier.Reverted);

        if (!string.IsNullOrWhiteSpace(report.ExecutiveSummary.AutomatedSummary))
        {
            sb.AppendLine();
            sb.Append("**Automated summary:** ").AppendLine(NormalizeParagraph(report.ExecutiveSummary.AutomatedSummary));
        }

        sb.AppendLine();

        // 3. Per incident
        sb.AppendLine("## Findings");
        sb.AppendLine();

        if (report.Incidents.Count == 0)
        {
            sb.AppendLine("_No findings included in this report._");
            sb.AppendLine();
        }
        else
        {
            for (var i = 0; i < report.Incidents.Count; i++)
            {
                var incident = report.Incidents[i];
                sb.Append("### ")
                    .Append((i + 1).ToString(CultureInfo.InvariantCulture))
                    .Append(". ")
                    .Append(NormalizeSingleLine(incident.Headline));
                if (incident.IsPinned)
                {
                    sb.Append(" [Pinned]");
                }

                sb.AppendLine();
                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(incident.ClaimOriginal))
                {
                    sb.Append("**Finnish original:** ")
                        .Append(NormalizeSingleLine(incident.ClaimOriginal));
                    if (incident.IsMachineTranslatedHeadline)
                    {
                        sb.Append(" _(Headline is machine translation)_");
                    }

                    sb.AppendLine();
                    sb.AppendLine();
                }

                sb.Append("- **Level ")
                    .Append(incident.TierInfo.Level.ToString(CultureInfo.InvariantCulture))
                    .Append(" — ")
                    .Append(incident.TierInfo.Label)
                    .Append(":** ")
                    .AppendLine(NormalizeSingleLine(incident.TierReason));

                sb.Append("- **Articles:** ").AppendLine(ReportBuilder.FormatArticlesPlain(incident.Articles));

                sb.Append("- **First seen:** ").Append(FormatUtc(incident.FirstSeen));
                if (incident.LastRemoved is { } lastRemoved)
                {
                    sb.Append(" · **Removed:** ").Append(FormatUtc(lastRemoved));
                }

                sb.Append(" · **Total live:** ").AppendLine(incident.TotalLiveText);
                sb.AppendLine();

                sb.AppendLine("#### Edit timeline");
                sb.AppendLine();
                if (incident.Edits.Count == 0)
                {
                    sb.AppendLine("_No edit timeline entries available._");
                }
                else
                {
                    foreach (var edit in incident.Edits)
                    {
                        sb.Append("- ")
                            .Append(FormatUtc(edit.Timestamp))
                            .Append(" · **")
                            .Append(edit.ActionLabel)
                            .Append("** · ")
                            .Append(edit.Editor.PlainText);

                        if (!string.IsNullOrWhiteSpace(edit.Summary))
                        {
                            sb.Append(" — ").Append('"').Append(NormalizeSingleLine(edit.Summary)).Append('"');
                        }

                        if (edit.RevisionUrl is not null)
                        {
                            sb.Append(" · ").Append(edit.RevisionUrl.AbsoluteUri);
                        }

                        sb.AppendLine();
                    }
                }

                sb.AppendLine();
                sb.AppendLine("#### Before / after passage");
                sb.AppendLine();
                if (incident.Passage is { } passage)
                {
                    sb.AppendLine("**Before:**");
                    AppendBlockquote(sb, string.IsNullOrWhiteSpace(passage.BeforeProse) ? "(empty before edit)" : passage.BeforeProse);
                    sb.AppendLine();
                    sb.AppendLine("**After:**");
                    AppendBlockquote(sb, string.IsNullOrWhiteSpace(passage.AfterProse) ? "(removed)" : passage.AfterProse);
                }
                else
                {
                    sb.AppendLine("_The text of this edit is not available right now._");
                }

                sb.AppendLine();

                if (!string.IsNullOrWhiteSpace(incident.PinNote))
                {
                    sb.Append("> **Note:** ").AppendLine(NormalizeSingleLine(incident.PinNote));
                    sb.AppendLine();
                }
            }
        }

        // 4. Appendix
        sb.AppendLine("## Appendix");
        sb.AppendLine();
        sb.AppendLine("### Methodology");
        sb.AppendLine();
        sb.AppendLine(report.Appendix.MethodologySummary);
        sb.AppendLine();
        sb.AppendLine("#### Evidence levels");
        sb.AppendLine();
        foreach (var level in report.Appendix.Levels)
        {
            sb.Append("- **Level ")
                .Append(level.Level.ToString(CultureInfo.InvariantCulture))
                .Append(" — ")
                .Append(level.Label)
                .Append(":** ")
                .AppendLine(level.Meaning);
        }

        sb.AppendLine();
        sb.AppendLine("### Limitations");
        sb.AppendLine();
        foreach (var limitation in report.Appendix.Limitations)
        {
            sb.Append("- ").AppendLine(limitation);
        }

        sb.AppendLine();
        sb.AppendLine("### Attribution and licence");
        sb.AppendLine();
        sb.AppendLine(report.Appendix.AttributionText);
        sb.AppendLine();
        sb.Append(report.Appendix.MachineTranslationNotice)
            .Append(" Licence: ")
            .Append(report.Appendix.License)
            .Append(" (")
            .Append(report.Appendix.LicenseUrl.AbsoluteUri)
            .AppendLine(").");
        sb.AppendLine();

        sb.AppendLine("### Cited revisions");
        sb.AppendLine();
        if (report.Appendix.CitedRevisions.Count == 0)
        {
            sb.AppendLine("_No revisions cited._");
        }
        else
        {
            foreach (var rev in report.Appendix.CitedRevisions)
            {
                sb.Append("- Revision ")
                    .Append(rev.RevId.ToString(CultureInfo.InvariantCulture))
                    .Append(": ")
                    .AppendLine(rev.Url.AbsoluteUri);
            }
        }

        return sb.ToString();
    }

    internal static string FormatUtc(DateTimeOffset timestamp) =>
        timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static void AppendTierCount(StringBuilder sb, InvestigationReport report, EvidenceTier tier)
    {
        var info = EvidenceTiers.Get(tier);
        var count = report.ExecutiveSummary.TierCounts.GetValueOrDefault(tier, 0);
        sb.Append("- **")
            .Append(info.Label)
            .Append(":** ")
            .AppendLine(count.ToString("N0", CultureInfo.InvariantCulture));
    }

    private static void AppendBlockquote(StringBuilder sb, string text)
    {
        foreach (var line in text.AsSpan().EnumerateLines())
        {
            if (line.IsEmpty)
            {
                sb.AppendLine(">");
            }
            else
            {
                sb.Append("> ").Append(line).AppendLine();
            }
        }
    }

    private static string NormalizeSingleLine(string value)
    {
        var parts = new List<string>();
        foreach (var line in value.AsSpan().EnumerateLines())
        {
            var trimmed = line.Trim();
            if (!trimmed.IsEmpty)
            {
                parts.Add(trimmed.ToString());
            }
        }

        return string.Join(" ", parts);
    }

    private static string NormalizeParagraph(string value)
    {
        var lines = new List<string>();
        foreach (var line in value.Trim().AsSpan().EnumerateLines())
        {
            lines.Add(line.ToString());
        }

        return string.Join(NewLineLf, lines);
    }
}
