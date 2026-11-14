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
    private static readonly Regex WikilinkRegex = new(@"\[\[([^\[\]|]*)(?:\|([^\[\]]*))?\]\]", RegexOptions.Compiled);
    private static readonly Regex CommentIpv4Regex = new(@"(?<!\d\.?)\d{1,3}(?:\.\d{1,3}){3}(?!\.?\d)", RegexOptions.Compiled);
    private static readonly Regex CommentIpv6Regex = new(@"(?<![0-9A-Za-z])(?<![0-9A-Fa-f]:)(?<!::)[0-9A-Fa-f:]*:[0-9A-Fa-f:]*(?![0-9A-Za-z:])", RegexOptions.Compiled);

    /// <summary>
    /// Edit summary → plain text safe to display: wikilinks flattened, every IPv4/IPv6 address masked exactly like For().
    /// </summary>
    public static string MaskComment(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return "";
        }

        // 1. Flatten wikilinks [[target|label]] → label (if non-blank), [[target]] / [[target|]] → target.
        var text = WikilinkRegex.Replace(comment, m =>
        {
            var target = m.Groups[1].Value;
            var label = m.Groups[2].Success ? m.Groups[2].Value : null;
            return !string.IsNullOrWhiteSpace(label) ? label : target;
        });

        // 2. IPv4: candidates (?<!\d\.?)\d{1,3}(?:\.\d{1,3}){3}(?!\.?\d); replace with For(m).DisplayName iff For(m).Kind == IPv4.
        text = CommentIpv4Regex.Replace(text, m =>
        {
            var label = For(m.Value);
            return label.Kind == EditorKind.IPv4 ? label.DisplayName : m.Value;
        });

        // 3. IPv6: candidates not starting mid-word or mid-address (a ":" right before is fine after a namespace like
        //    "User talk:"), containing ≥ 2 : and ≥ 1 hex digit;
        //    replace with For(c).DisplayName iff Kind == IPv6. If it doesn't parse and ends with :, retry once without the last :.
        text = CommentIpv6Regex.Replace(text, m =>
        {
            var c = m.Value;
            int colonCount = 0;
            bool hasHex = false;
            foreach (char ch in c)
            {
                if (ch == ':')
                {
                    colonCount++;
                }
                else if ((ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F'))
                {
                    hasHex = true;
                }
            }

            if (colonCount < 2 || !hasHex)
            {
                return c;
            }

            var label = For(c);
            if (label.Kind == EditorKind.IPv6)
            {
                return label.DisplayName;
            }

            if (c.EndsWith(':'))
            {
                var withoutColon = c[..^1];
                var retry = For(withoutColon);
                if (retry.Kind == EditorKind.IPv6)
                {
                    return retry.DisplayName + ":";
                }
            }

            return c;
        });

        return text;
    }

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
