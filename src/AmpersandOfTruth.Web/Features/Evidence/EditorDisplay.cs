namespace AmpersandOfTruth.Web.Features.Evidence;

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

public enum EditorKind { Named, Temporary, IPv4, IPv6, Hidden }

/// <summary>
/// GroupingKey: two edits with equal keys count as ONE editor (used for "added by n different editors").
/// </summary>
public sealed record EditorLabel(EditorKind Kind, string DisplayName, string? Qualifier, Uri? ProfileUrl, string GroupingKey)
{
    public bool IsAnonymous => Kind is EditorKind.IPv4 or EditorKind.IPv6 or EditorKind.Temporary;

    /// <summary>
    /// "85.76.x.x (anonymous)", "~2025-26183-73 (temporary account)", "Matti Meikäläinen", "Hidden editor"
    /// </summary>
    public string PlainText => Qualifier is null ? DisplayName : $"{DisplayName} ({Qualifier})";
}

public static class EditorDisplay
{
    private static readonly Regex TemporaryAccountRegex = new(@"^~\d{4}-\d+-\d+$", RegexOptions.Compiled);
    private static readonly Regex Ipv4Regex = new(@"^\d{1,3}(\.\d{1,3}){3}$", RegexOptions.Compiled);

    /// <param name="user">raw corpus.revision.user (may be null)</param>
    public static EditorLabel For(string? user)
    {
        // 1. null / empty / whitespace
        if (string.IsNullOrWhiteSpace(user))
        {
            return new EditorLabel(EditorKind.Hidden, "Hidden editor", null, null, "hidden");
        }

        var trimmed = user.Trim();

        // 2. regex ^~\d{4}-\d+-\d+$ (e.g. ~2025-26183-73)
        if (TemporaryAccountRegex.IsMatch(trimmed))
        {
            return new EditorLabel(
                EditorKind.Temporary,
                trimmed,
                "temporary account",
                WikipediaLinks.Contributions(trimmed),
                $"temp:{trimmed}");
        }

        // 3. regex ^\d{1,3}(\.\d{1,3}){3}$ AND IPAddress.TryParse → InterNetwork
        if (Ipv4Regex.IsMatch(trimmed) &&
            IPAddress.TryParse(trimmed, out var ip4) &&
            ip4.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip4.GetAddressBytes();
            return new EditorLabel(
                EditorKind.IPv4,
                $"{bytes[0]}.{bytes[1]}.x.x",
                "anonymous",
                null,
                $"ip4:{bytes[0]}.{bytes[1]}");
        }

        // 4. contains : AND IPAddress.TryParse → InterNetworkV6
        if (trimmed.Contains(':') &&
            IPAddress.TryParse(trimmed, out var ip6) &&
            ip6.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip6.GetAddressBytes();
            ushort g1 = (ushort)((bytes[0] << 8) | bytes[1]);
            ushort g2 = (ushort)((bytes[2] << 8) | bytes[3]);
            string g1Str = g1.ToString("X", CultureInfo.InvariantCulture);
            string g2Str = g2.ToString("X", CultureInfo.InvariantCulture);

            return new EditorLabel(
                EditorKind.IPv6,
                $"{g1Str}:{g2Str}:\u2026",
                "anonymous",
                null,
                $"ip6:{g1Str}:{g2Str}");
        }

        // 5. anything else
        return new EditorLabel(
            EditorKind.Named,
            trimmed,
            null,
            WikipediaLinks.Contributions(trimmed),
            $"user:{trimmed}");
    }
}
