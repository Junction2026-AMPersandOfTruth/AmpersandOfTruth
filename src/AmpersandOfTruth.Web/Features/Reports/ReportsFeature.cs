using System.Text;

namespace AmpersandOfTruth.Web.Features.Reports;

public static class ReportsFeature
{
    public static IServiceCollection AddReportsFeature(this IServiceCollection services)
    {
        services.AddSingleton<ReportBuilder>();
        return services;
    }

    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/library/{id:guid}/report.md", async (
            Guid id,
            HttpContext http,
            ReportBuilder builder,
            CancellationToken ct) =>
        {
            var pinnedOnly = http.Request.Query.TryGetValue("pinned", out var raw) && IsPinnedQuery(raw.ToString());
            var report = await builder.BuildAsync(id, new ReportOptions(PinnedOnly: pinnedOnly, IncludeSummary: true), ct);
            if (report is null)
            {
                return Results.NotFound();
            }

            var markdown = MarkdownReport.Render(report);
            var slug = SanitizeFileNameSlug(report.TopicSlug);
            http.Response.Headers.ContentDisposition = string.Concat("attachment; filename=", '"', slug, "-report.md", '"');
            return Results.Text(markdown, "text/markdown", Encoding.UTF8);
        });

        return endpoints;
    }

    internal static bool IsPinnedQuery(string? value) =>
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    internal static string SanitizeFileNameSlug(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "investigation";
        }

        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch is '-' or '_')
            {
                sb.Append(ch);
            }
        }

        var cleaned = sb.ToString().Trim('-');
        return cleaned.Length > 0 ? cleaned : "investigation";
    }
}
