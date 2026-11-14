namespace AmpersandOfTruth.Web.Features.Search;

public sealed record SuggestedTopic(string Label, string Topic);

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

    public List<SuggestedTopic> SuggestedTopics { get; set; } = [.. DefaultSuggestedTopics];
}
