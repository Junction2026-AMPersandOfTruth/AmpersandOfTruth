namespace AmpersandOfTruth.Web.Features.Evidence;

using System.Globalization;

public static class WikipediaLinks
{
    public const string BaseUrl = "https://fi.wikipedia.org";

    public static Uri Revision(long revId)
    {
        if (revId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revId), revId, "Revision ID must be positive.");
        }

        return new Uri($"{BaseUrl}/w/index.php?oldid={revId.ToString(CultureInfo.InvariantCulture)}");
    }

    public static Uri Diff(long revId)
    {
        if (revId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revId), revId, "Revision ID must be positive.");
        }

        return new Uri($"{BaseUrl}/w/index.php?diff=prev&oldid={revId.ToString(CultureInfo.InvariantCulture)}");
    }

    public static Uri Contributions(string user)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            throw new ArgumentException("User must not be null or blank.", nameof(user));
        }

        return new Uri($"{BaseUrl}/wiki/Special:Contributions/{EscapeTitleOrUser(user)}");
    }

    public static Uri Article(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title must not be null or blank.", nameof(title));
        }

        return new Uri($"{BaseUrl}/wiki/{EscapeTitleOrUser(title)}");
    }

    private static string EscapeTitleOrUser(string value)
    {
        var withUnderscores = value.Replace(' ', '_');
        var escaped = Uri.EscapeDataString(withUnderscores);
        return escaped
            .Replace("%2F", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("%3A", ":", StringComparison.OrdinalIgnoreCase);
    }
}
