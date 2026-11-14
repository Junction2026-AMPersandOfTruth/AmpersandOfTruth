using System.Text;
using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// Edit summaries as the LLM may see them (evidence standard §3: no account names leave the app). fi.wikipedia revert
/// summaries name the undone editor ("…, jonka teki X", "Käyttäjän X muokkaukset kumottiin…", "(tehneet X, Y)"), so
/// <see cref="EditorDisplay.MaskComment"/> (IPs only) is not enough:
/// <list type="number">
/// <item>every involved account (span authors, reverter, re-inserters, re-removers; named or temporary) becomes
/// <c>Editor n</c>, numbered in that order — the same account keeps its number across all summaries of one edit;</item>
/// <item>user links and the editor slots of the revert templates that name anyone else become <c>[editor]</c>;</item>
/// <item>then IPs are masked as everywhere else.</item>
/// </list>
/// Ported from <c>research/22-judge/evidence.py</c> <c>format_summary</c> and <c>research/36-s9-classify/inputs.py</c>
/// <c>mask_summary</c>. Over-masking a word is acceptable; leaking a name is not. Pure and thread-safe.
/// </summary>
public sealed class PromptEditors
{
    public const string Unknown = "[editor]";

    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    /// <summary>The involved accounts in numbering order. IPs and hidden users get no label (IPs are masked anyway).</summary>
    public PromptEditors(IEnumerable<string?> involved)
    {
        var byGroup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var user in involved)
        {
            var label = EditorDisplay.For(user);
            if (label.Kind is not (EditorKind.Named or EditorKind.Temporary)) continue;
            if (!byGroup.TryGetValue(label.GroupingKey, out var pseudonym))
                byGroup[label.GroupingKey] = pseudonym = $"Editor {byGroup.Count + 1}";
            var name = user!.Trim();
            _labels[name] = pseudonym;
            _labels[name.Replace('_', ' ')] = pseudonym;
            _labels[name.Replace(' ', '_')] = pseudonym;
        }
    }

    /// <summary>The involved accounts of one removed edit, in the order they are numbered.</summary>
    public static PromptEditors For(EditReading reading) => new(
        [.. reading.Span.Select(r => r.User), reading.Edit.Revert.User,
         .. reading.Reinsertions.Select(r => r.User), .. reading.ReRemovals.Select(r => r.User)]);

    public string? LabelOf(string? user) => user is null ? null : _labels.GetValueOrDefault(user.Trim());

    // ── links ──
    private const string UserNs = @"(?:User|Käyttäjä)";
    private const string TalkNs = @"(?:User[ _]talk|Keskustelu[ _]käyttäjästä|Käyttäjä[ _]keskustelu)";
    private const string ContribNs = @"(?:Special|Toiminnot|Spesiaali)\s*:\s*(?:Contributions|Muokkaukset)/";

    private static readonly Regex TalkLink = new(
        @"\[\[\s*:?\s*" + TalkNs + @"\s*:([^|\]]*)(?:\|([^\]]*))?\]\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UserLink = new(
        @"\[\[\s*:?\s*(?:" + UserNs + @"\s*:|" + ContribNs + @")([^|\]#]*)(?:[#|][^\]]*)?\]\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // A summary cut at 255 bytes in the middle of a user link: everything after the link start goes.
    private static readonly Regex TruncatedUserLink = new(
        @"\[\[\s*:?\s*(?:" + TalkNs + @"|" + UserNs + @"|" + ContribNs.TrimEnd('/') + @")\s*[:/][^\]]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // ── what MaskComment leaves ──
    private static readonly Regex MaskedIp = new(@"\b\d{1,3}\.\d{1,3}\.x\.x\b|\b[0-9A-F]{1,4}:[0-9A-F]{1,4}:…", RegexOptions.CultureInvariant);
    private static readonly Regex TemporaryAccount = new(@"~\d{4}-\d+-\d+", RegexOptions.CultureInvariant);

    // ── editor slots of the revert templates (MediaWiki, FlaggedRevs, Twinkle/popups) and common hand-written forms ──
    private const string Name = @"[^()\[\]<>:,;\u0000]+?";
    private const string Token = @"[^\s,;:()\[\]""\u0000.]+";
    private static readonly Regex TehneetList = new(@"(\((?:tehneet|tehnyt|käyttäjältä|käyttäjiltä) )([^()\[\]<>]+?)(\))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ListSplit = new(@"(\s*,\s*|\s+ja\s+|\s+and\s+)", RegexOptions.CultureInvariant);
    private static readonly Regex[] Slots =
    [
        new(@"(\bdated [\d\-: ]+ by )(" + Name + @")( using\b|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\bjonka (?:on )?(?:teki|tehnyt) )(" + Name + @")( \(|[,:;]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\bkäyttäjän )(" + Name + @")( \(keskustelu\)| tekemään versioon| tekemään muutokseen| muokkaukset\b| muokkaus\b| muokkaukseen\b| versioon\b| tekemä\b| tekemät\b)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\bkäyttäjältä )(" + Name + @")(:| \(|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\beston\s+kiertoa\s*\(\s*)([^)\u0000]+?)(\s*\))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\bPalautettiin )(" + Name + @")(:n muokkaukset)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(^|[\s(])(" + Token + @")(:n (?:muokkaukset|vandalismi) kumottiin)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\b(?:viimeisimpään\s+käyttäjän|estetyn\s+käyttäjän|jonka\s+on\s+tehnyt|jonka\s+teki)\s+)(" + Token + ")",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\b(?:Reverted\s+(?:\d+\s+)?edits?|edits|version|revision(?:\s+\d+)?)\s+by\s+)(" + Token + ")",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new(@"(\b" + UserNs + @":)(" + Token + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
    ];

    /// <summary>One raw edit summary → the text the classifier prompt gets. Empty for no summary.</summary>
    public string Mask(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return "";
        var s = comment.Replace("\u0000", "");

        // 1. Links: talk links → their label; user/contribution links → the involved name, an IP (both handled below) or [editor].
        s = TalkLink.Replace(s, m => m.Groups[2].Value.Trim() is { Length: > 0 } label ? label : "keskustelu");
        s = UserLink.Replace(s, m => LinkTarget(m.Groups[1].Value));
        s = TruncatedUserLink.Replace(s, Unknown);

        // 2. IPs masked, other wikilinks flattened.
        s = EditorDisplay.MaskComment(s);

        // 3. Protect masked IPs, then put the involved accounts' labels in (longest name first), also protected.
        var held = new List<string>();
        string Hold(string value)
        {
            held.Add(value);
            return $"\u0000{held.Count - 1}\u0000";
        }
        s = MaskedIp.Replace(s, m => Hold(m.Value));
        foreach (var name in _labels.Keys.Where(n => n.Length >= 2).OrderByDescending(n => n.Length).ThenBy(n => n, StringComparer.Ordinal))
        {
            var pattern = new Regex(@"(?<![\w\u0000])" + Regex.Escape(name) + @"(?=(?::n|'s|n)?(?![\w]))", RegexOptions.CultureInvariant);
            s = pattern.Replace(s, _ => Hold(_labels[name]));
        }
        s = TemporaryAccount.Replace(s, Unknown);

        // 4. Anyone else named in an editor slot.
        s = TehneetList.Replace(s, m =>
        {
            var parts = ListSplit.Split(m.Groups[2].Value);
            var sb = new StringBuilder(m.Groups[1].Value);
            for (var i = 0; i < parts.Length; i++)
                sb.Append(i % 2 == 1 || IsKept(parts[i]) ? parts[i] : Unknown);
            return sb.Append(m.Groups[3].Value).ToString();
        });
        foreach (var slot in Slots)
            s = slot.Replace(s, m => IsKept(m.Groups[2].Value) ? m.Value : m.Groups[1].Value + Unknown + m.Groups[3].Value);

        // 5. Restore what was held.
        for (var i = held.Count - 1; i >= 0; i--)
            s = s.Replace($"\u0000{i}\u0000", held[i]);
        return string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>A slot that already holds a label, an [editor] or a masked IP, or nothing, is left as it is.</summary>
    private static bool IsKept(string candidate)
    {
        var c = candidate.Trim();
        return c.Length == 0 || c.Contains('\u0000') || c.Contains(Unknown, StringComparison.Ordinal);
    }

    /// <summary>A user link → its target if involved (labelled and protected in step 3) or an IP (masked in step 2), else [editor].</summary>
    private string LinkTarget(string rawTarget)
    {
        var target = rawTarget.Trim();
        if (LabelOf(target) is not null) return target;
        var display = EditorDisplay.For(target.Replace('_', ' '));
        return display.Kind is EditorKind.IPv4 or EditorKind.IPv6 ? target : Unknown;
    }
}
