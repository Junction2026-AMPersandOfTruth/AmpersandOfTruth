using System.Globalization;
using System.Text;

namespace AmpersandOfTruth.Web.Features.Investigations.View;

/// <summary>Where incidents come from (#98 §1). Config <c>Investigations:Source</c>.</summary>
public enum IncidentSourceKind
{
    /// <summary>Stages 1–2 real (articles, removed edits), stages 3–4 replay hand-written sample incidents. Default until #39 lands.</summary>
    Fixtures,

    /// <summary>The real pipeline (<c>InvestigationPipeline</c>, #39).</summary>
    Pipeline,
}

public sealed class InvestigationSourceOptions
{
    public const string Section = "Investigations";

    public IncidentSourceKind Source { get; set; } = IncidentSourceKind.Fixtures;

    /// <summary>True → every page that shows incidents must show the "Sample incidents: not findings" banner.</summary>
    public bool IsSampleData => Source == IncidentSourceKind.Fixtures;
}

/// <summary>
/// The ranked incidents of a topic for pages that don't run the stream themselves (incident page prev/next, article
/// markers). Fixture mode: the sample incidents. Pipeline mode: the last completed run of that topic in this process
/// (persistent run cache is #40). null = nothing known for that topic yet → the page offers to open the investigation.
/// </summary>
public interface IIncidentSource
{
    Task<IReadOnlyList<IncidentView>?> ForTopicAsync(string topicSlug, CancellationToken ct = default);
}

/// <summary>Topic text ↔ URL slug for <c>/investigate/{topic}</c> (docs/design/plan/1-flow-and-routes.md).</summary>
public static class TopicSlug
{
    public const int MaxTopicLength = 120;

    /// <summary>
    /// Lower-case; letters (incl. ä, ö, å) and digits kept; every other run of characters becomes one '-'; trimmed of '-'.
    /// "Winter War" → "winter-war", "Suomen sisällissota 1918" → "suomen-sisällissota-1918". Empty when nothing is left.
    /// </summary>
    public static string From(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        var sb = new StringBuilder(topic.Length);
        var dash = false;
        foreach (var ch in topic.Trim().Normalize(NormalizationForm.FormC).ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (dash && sb.Length > 0) sb.Append('-');
                sb.Append(ch);
                dash = false;
            }
            else dash = true;
        }
        return sb.ToString();
    }

    /// <summary>The text to search for a slug: dashes become spaces ("winter-war" → "winter war").</summary>
    public static string ToQuery(string slug) => slug.Replace('-', ' ').Trim();

    /// <summary>Route of the investigation page for a topic or slug.</summary>
    public static string Route(string topicOrSlug) => "/investigate/" + Uri.EscapeDataString(From(topicOrSlug));
}
