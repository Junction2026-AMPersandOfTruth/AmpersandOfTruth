namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Typed output of the <c>expand-topic</c> prompt (P1.R3 #24, chosen by R6 #27; mirrors <c>Prompts/expand-topic.schema.json</c>,
/// a test keeps them in sync). One cached LLM call per distinct topic text.
/// </summary>
/// <param name="InScope">The model's view of whether the corpus (Finnish history, politics, society) covers the topic. R6 shows
/// it as a warning only: three out-of-corpus controls are too few to block a search.</param>
/// <param name="TopicFi">The topic in idiomatic Finnish.</param>
/// <param name="Reformulations">3–5 short search queries, mostly Finnish.</param>
/// <param name="HydeFi">A hypothetical Finnish article lead (R3-C; unused by mode D, kept for the record).</param>
/// <param name="TitleGuesses">Likely exact fi Wikipedia titles, most central first (empty when out of scope).</param>
/// <param name="KeywordsFi">FULLTEXT prefix stems (R2-D; unused by mode D).</param>
/// <param name="Angle">The contested aspect asked about, or "general".</param>
public sealed record TopicExpansion(
    bool InScope,
    string ScopeReason,
    string TopicFi,
    IReadOnlyList<string> Reformulations,
    string HydeFi,
    IReadOnlyList<string> TitleGuesses,
    IReadOnlyList<string> KeywordsFi,
    string Angle);

/// <summary>Variable names of <c>expand-topic</c>, for callers building requests.</summary>
public static class ExpandTopicPrompt
{
    public const string Id = "expand-topic";

    public static Dictionary<string, string> Variables(string topic) => new() { ["topic"] = topic };
}
