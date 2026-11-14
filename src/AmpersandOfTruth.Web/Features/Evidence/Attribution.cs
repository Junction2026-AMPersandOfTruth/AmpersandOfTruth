namespace AmpersandOfTruth.Web.Features.Evidence;

using System.Globalization;

public static class Attribution
{
    public const string License = "CC BY-SA 4.0";

    public static Uri LicenseUrl { get; } = new("https://creativecommons.org/licenses/by-sa/4.0/");

    public const string FooterText =
        "Wikipedia text © its contributors, CC BY-SA 4.0, quoted from fi.wikipedia.org revisions linked on each passage. " +
        "Data: Wikimedia dumps, FineWiki, MariaDB Foundation datasets; embeddings: bge-m3. " +
        "Translations and assessments are automated and can be wrong.";

    public const string FooterLinkText = "How this works";

    public const string MethodologyRoute = "/methodology";

    public const string AutomatedAssessmentLabel = "Automated assessment";

    public const string MachineTranslationNotice =
        "Machine translation of Wikipedia text, shared under CC BY-SA 4.0. The original Finnish is one click away.";

    public static string ForRevision(long revId, DateTimeOffset timestamp, string editorPlainText) =>
        $"fi.wikipedia.org revision {revId.ToString(CultureInfo.InvariantCulture)}, {timestamp.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC, by {editorPlainText}. {License}.";
}
