namespace AmpersandOfTruth.Web.Features.Search;

public sealed record SuggestedTopic(string Label, string Topic);

public sealed class ScopeOptions
{
    public const double DefaultMaxTopDistance = 0.495;
    public const double DefaultMaxMeanDistance = 0.515;
    public const int DefaultMaxSuggestions = 4;

    public double MaxTopDistance { get; set; } = DefaultMaxTopDistance;
    public double MaxMeanDistance { get; set; } = DefaultMaxMeanDistance;
    public int MaxSuggestions { get; set; } = DefaultMaxSuggestions;
}

/// <summary>Bound from the <c>Search</c> config section.</summary>
public sealed class SearchOptions
{
    public const string Section = "Search";

    public static IReadOnlyList<SuggestedTopic> DefaultSuggestedTopics { get; } =
    [
        new("Civil War 1918", "Civil War 1918"),
        new("Winter War", "Winter War"),
        new("Continuation War", "Continuation War"),
        new("Mannerheim", "Mannerheim"),
        new("Mainila shots", "Mainila shots"),
        new("Ceded Karelia", "Ceded Karelia"),
        new("Finlandization", "Finlandization"),
    ];

    public ScopeOptions Scope { get; set; } = new();

    public List<SuggestedTopic> SuggestedTopics { get; set; } = [.. DefaultSuggestedTopics];
}
