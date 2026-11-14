namespace AmpersandOfTruth.Web.Features.Demo;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    public const string SafeModeMissMessage =
        "This topic hasn't been analysed for the demo. Try one of the suggested topics.";

    public static IReadOnlyList<string> DefaultSeedTopics { get; } =
    [
        "Winter War",
        "Continuation War",
        "Finnish Civil War",
    ];

    public bool SafeMode { get; set; }

    public int MaxConcurrentColdRuns { get; set; } = 2;

    public int StartsPerClientPerMinute { get; set; } = 6;

    public bool SeedLibrary { get; set; }

    public List<string> SeedTopics { get; set; } = [.. DefaultSeedTopics];
}
