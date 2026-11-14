namespace AmpersandOfTruth.Web.Features.Evidence;

public static class Methodology
{
    public const string Summary =
        "Ampersand of Truth searches the edit history of Finnish Wikipedia articles about the history of Finland for text that was added and later taken out by other editors. " +
        "For each finding it shows what was added, when, by which account, how long it stayed visible and how it was removed, with a link to every revision on Wikipedia. " +
        "Each finding gets one of three levels based only on that history. " +
        "Automated assessments help read the Finnish text and recognise factual claims. " +
        "They are labelled as such and shown next to the quoted text they rely on.";

    public static IReadOnlyList<string> Limitations { get; } =
    [
        "Removed does not mean false, and kept does not mean true. We only see what Wikipedia's editors caught.",
        "Edit history cannot show intent. A pattern consistent with coordination can also be one persistent person, a shared network, or several people who honestly share a view.",
        "We only see our dataset: 19,742 Finnish Wikipedia articles in the History of Finland category tree, 2003 to 1 October 2026. Activity elsewhere on Wikipedia or the web is invisible.",
        "One IP address can be many people, and one person can use many addresses. We mask addresses and count each masked range as one editor.",
        "Translations and automated assessments can be wrong. The original Finnish is always one click away.",
    ];

    public const string AccountsCaveat = "Only edits within our dataset of 19,742 articles are shown.";
}
