using System.Globalization;
using System.Net;
using System.Text;

namespace AmpersandOfTruth.Web.Features.WikiDiff;

internal enum TokenClass : byte { Prose, Markup, Template, Reference, Category, Interwiki, File, Separator }

/// <summary>
/// One diffable unit of wikitext. <see cref="Key"/> is what the diff compares (class + whitespace-normalised text), so a
/// word in running text never matches the same word inside a template. <see cref="Prose"/> is the token's
/// contribution to the prose view (empty for markup).
/// </summary>
internal readonly record struct WikiToken(
    int Start, int End, TokenClass Class, string Key, string Prose, int Block, bool IsSpace, bool IsWord)
{
    public static WikiToken Separator(int block) => new(0, 0, TokenClass.Separator, "¶", "", block, false, false);
}

/// <summary>
/// Markup-aware tokenizer for one block of wikitext. Atomic tokens: a whole <c>&lt;ref&gt;…&lt;/ref&gt;</c> (its prose is the
/// flattened citation), a category, interwiki or file link, a comment, an HTML tag. Templates are tokenized inside, so an
/// infobox edit is reported word by word; values of a multi-line template (an infobox) count as prose, inline templates
/// (<c>{{Lähde}}</c>, <c>{{Viitteet}}</c>) do not. Link labels are prose, link targets markup. Everything else is words,
/// whitespace and single punctuation characters.
/// </summary>
internal sealed class WikitextTokenizer
{
    private enum Ctx { Prose, Template, TemplateValue }

    private readonly string _s;
    private readonly int _block;
    private readonly bool _citation;
    private readonly List<WikiToken> _out;

    private WikitextTokenizer(string s, int block, bool citation, List<WikiToken> output)
    {
        _s = s;
        _block = block;
        _citation = citation;
        _out = output;
    }

    public static void Tokenize(string s, int start, int end, int block, List<WikiToken> output) =>
        new WikitextTokenizer(s, block, citation: false, output).Scan(start, end, Ctx.Prose, start);

    /// <summary>The readable text of a reference: its prose plus every template parameter value, joined with "; ".</summary>
    public static string CitationText(string s, int start, int end)
    {
        var tokens = new List<WikiToken>();
        new WikitextTokenizer(s, -1, citation: true, tokens).Scan(start, end, Ctx.Prose, start);
        var sb = new StringBuilder();
        foreach (var t in tokens) sb.Append(t.Prose);
        return TextUtil.CleanValues(sb.ToString());
    }

    private void Scan(int i, int end, Ctx ctx, int lineFloor)
    {
        while (i < end)
        {
            char c = _s[i];
            bool lineStart = ctx == Ctx.Prose && (i == lineFloor || (i > 0 && _s[i - 1] == '\n'));
            int j;

            switch (c)
            {
                case '<':
                    if (At(i, "<!--"))
                    {
                        j = _s.IndexOf("-->", i + 4, end - (i + 4), StringComparison.Ordinal);
                        j = j < 0 ? end : j + 3;
                        Add(i, j, TokenClass.Markup, ctx, "");
                        i = j;
                        continue;
                    }
                    if ((j = RefEnd(i, end)) > 0)
                    {
                        AddRef(i, j);
                        i = j;
                        continue;
                    }
                    if ((j = TagEnd(i, end)) > 0)
                    {
                        Add(i, j, TokenClass.Markup, ctx, "");
                        i = j;
                        continue;
                    }
                    break;
                case '{' when i + 1 < end && _s[i + 1] == '{':
                    j = MatchPair(i, end, '{', '}');
                    if (j > 0) ScanTemplate(i, j, ctx);
                    else Add(i, j = i + 2, TokenClass.Markup, ctx, "");
                    i = j;
                    continue;
                case '[' when i + 1 < end && _s[i + 1] == '[':
                    j = MatchPair(i, end, '[', ']');
                    if (j > 0) ScanLink(i, j, ctx);
                    else Add(i, j = i + 2, TokenClass.Markup, ctx, "");
                    i = j;
                    continue;
                case '[':
                    if ((j = ExternalLinkEnd(i, end)) > 0)
                    {
                        ScanExternalLink(i, j, ctx);
                        i = j;
                        continue;
                    }
                    break;
                case '\'' when i + 1 < end && _s[i + 1] == '\'':
                    j = i;
                    while (j < end && _s[j] == '\'') j++;
                    Add(i, j, TokenClass.Markup, ctx, "");
                    i = j;
                    continue;
                case '&':
                    if ((j = EntityEnd(i, end)) > 0)
                    {
                        var decoded = WebUtility.HtmlDecode(_s[i..j]);
                        Add(i, j, TokenClass.Prose, ctx, decoded.Length == 1 && char.IsWhiteSpace(decoded[0]) ? " " : decoded);
                        i = j;
                        continue;
                    }
                    break;
                case '_':
                    if ((j = MagicWordEnd(i, end)) > 0)
                    {
                        Add(i, j, TokenClass.Markup, ctx, "");
                        i = j;
                        continue;
                    }
                    break;
                case 'h' or 'H' or 'f' or 'F':
                    if ((j = BareUrlEnd(i, end)) > 0)
                    {
                        Add(i, j, TokenClass.Markup, ctx, _s[i..j]);   // a bare URL is shown as its own text
                        i = j;
                        continue;
                    }
                    break;
            }

            if (lineStart && (j = LineStartMarkupEnd(i, end)) > i)
            {
                Add(i, j, TokenClass.Markup, ctx, "");
                i = j;
                continue;
            }
            if (ctx == Ctx.Prose && c == '=')
            {
                j = i;
                while (j < end && _s[j] == '=') j++;
                if (j - i >= 2 && (lineStart || RestOfLineBlank(j, end)))
                {
                    Add(i, j, TokenClass.Markup, ctx, "");
                    i = j;
                    continue;
                }
            }
            if (ctx == Ctx.Prose && (c == '|' || c == '!') && i + 1 < end && _s[i + 1] == c && InTableLine(i))
            {
                Add(i, i + 2, TokenClass.Markup, ctx, "");
                i += 2;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                j = i + 1;
                while (j < end && char.IsWhiteSpace(_s[j])) j++;
                Add(i, j, TokenClass.Prose, ctx, " ", isSpace: true);
                i = j;
                continue;
            }
            if (IsWordChar(c))
            {
                j = i + 1;
                while (j < end && IsWordChar(_s[j])) j++;
                Add(i, j, TokenClass.Prose, ctx, _s[i..j], isWord: true);
                i = j;
                continue;
            }
            j = char.IsHighSurrogate(c) && i + 1 < end ? i + 2 : i + 1;
            Add(i, j, TokenClass.Prose, ctx, _s[i..j]);
            i = j;
        }
    }

    // ── Constructs ─────────────────────────────────────────────────────────────────────────────────────────────────

    private void ScanTemplate(int i, int j, Ctx ctx)
    {
        // An infobox is a template that spans lines; its parameter values are numbers, dates and names shown on the page.
        bool valued = ctx == Ctx.Prose && (_citation || _s.IndexOf('\n', i, j - i) >= 0);
        var valueCtx = valued ? Ctx.TemplateValue : Ctx.Template;
        int stop = j - 2;

        AddFixed(i, i + 2, TokenClass.Template, "");
        int bar = NextTopLevel(i + 2, stop, '|');
        Scan(i + 2, bar < 0 ? stop : bar, Ctx.Template, -1);
        while (bar >= 0)
        {
            AddFixed(bar, bar + 1, TokenClass.Template, valued ? "; " : "");
            int partStart = bar + 1;
            int next = NextTopLevel(partStart, stop, '|');
            int partEnd = next < 0 ? stop : next;
            int eq = NextTopLevel(partStart, partEnd, '=');
            if (eq >= 0 && eq - partStart <= 60 && _s.IndexOf('\n', partStart, eq - partStart) < 0)
            {
                Scan(partStart, eq, Ctx.Template, -1);
                AddFixed(eq, eq + 1, TokenClass.Template, "");
                Scan(eq + 1, partEnd, valueCtx, -1);
            }
            else
            {
                Scan(partStart, partEnd, valueCtx, -1);
            }
            bar = next;
        }
        AddFixed(stop, j, TokenClass.Template, "");
    }

    private void ScanLink(int i, int j, Ctx ctx)
    {
        int innerStart = i + 2, innerEnd = j - 2;
        int bar = NextTopLevel(innerStart, innerEnd, '|');
        int targetEnd = bar < 0 ? innerEnd : bar;
        var kind = TextUtil.LinkKind(_s.AsSpan(innerStart, targetEnd - innerStart));
        if (kind != TokenClass.Prose)
        {
            AddAtomic(i, j, kind, "");
            return;
        }

        Add(i, innerStart, TokenClass.Markup, ctx, "");
        if (bar >= 0)
        {
            Add(innerStart, bar + 1, TokenClass.Markup, ctx, "");   // target + '|': which page, not what is shown
            Scan(bar + 1, innerEnd, ctx, -1);
        }
        else
        {
            Scan(innerStart, innerEnd, ctx, -1);
        }
        Add(innerEnd, j, TokenClass.Markup, ctx, "");
    }

    private void ScanExternalLink(int i, int j, Ctx ctx)
    {
        int urlEnd = i + 1;
        while (urlEnd < j - 1 && !char.IsWhiteSpace(_s[urlEnd])) urlEnd++;
        Add(i, urlEnd, TokenClass.Markup, ctx, "");
        Scan(urlEnd, j - 1, ctx, -1);
        Add(j - 1, j, TokenClass.Markup, ctx, "");
    }

    private void AddRef(int i, int j)
    {
        int gt = _s.IndexOf('>', i, j - i);
        string prose = "";
        if (_s[gt - 1] != '/')
        {
            int close = _s.LastIndexOf("</", j - 1, j - gt, StringComparison.Ordinal);
            if (close > gt) prose = CitationText(_s, gt + 1, close);
        }
        AddAtomic(i, j, TokenClass.Reference, prose);
    }

    // ── Token construction ─────────────────────────────────────────────────────────────────────────────────────────

    private void Add(int start, int end, TokenClass baseClass, Ctx ctx, string prose, bool isSpace = false, bool isWord = false)
    {
        var cls = ctx == Ctx.Prose ? baseClass : TokenClass.Template;
        if (ctx == Ctx.Template) prose = "";
        char tag = baseClass == TokenClass.Markup ? 'm' : 'p';
        string key = isSpace ? $"{Prefix(cls)}{tag} " : $"{Prefix(cls)}{tag}{TextUtil.Collapse(_s.AsSpan(start, end - start))}";
        _out.Add(new WikiToken(start, end, cls, key, prose, _block, isSpace, isWord));
    }

    private void AddFixed(int start, int end, TokenClass cls, string prose) =>
        _out.Add(new WikiToken(start, end, cls, $"{Prefix(cls)}m{_s.AsSpan(start, end - start)}", prose, _block, false, false));

    private void AddAtomic(int start, int end, TokenClass cls, string prose) =>
        _out.Add(new WikiToken(start, end, cls, $"{Prefix(cls)}{TextUtil.Collapse(_s.AsSpan(start, end - start))}", prose, _block, false, false));

    private static char Prefix(TokenClass cls) => (char)('A' + (int)cls);

    // ── Matchers (each returns the end index, or -1 / a value ≤ start when there is no match) ───────────────────────

    private bool At(int i, string lit) => string.CompareOrdinal(_s, i, lit, 0, lit.Length) == 0;

    private bool AtIgnoreCase(int i, string lit) =>
        i + lit.Length <= _s.Length && _s.AsSpan(i, lit.Length).Equals(lit, StringComparison.OrdinalIgnoreCase);

    /// <summary>End of the balanced <c>{{…}}</c> or <c>[[…]]</c> that starts at <paramref name="i"/>, or -1.</summary>
    private int MatchPair(int i, int end, char open, char close)
    {
        int depth = 0;
        int k = i;
        while (k < end - 1)
        {
            char ch = _s[k];
            if (ch == open && _s[k + 1] == open)
            {
                depth++;
                k += 2;
            }
            else if (ch == close && _s[k + 1] == close)
            {
                depth--;
                k += 2;
                if (depth == 0) return k;
            }
            else
            {
                k++;
            }
        }
        return -1;
    }

    /// <summary>First <paramref name="target"/> char in [p, stop) outside nested templates, links, comments and refs.</summary>
    private int NextTopLevel(int p, int stop, char target)
    {
        int k = p;
        while (k < stop)
        {
            char ch = _s[k];
            int skip = -1;
            if (ch == '{' && k + 1 < stop && _s[k + 1] == '{') skip = MatchPair(k, stop, '{', '}');
            else if (ch == '[' && k + 1 < stop && _s[k + 1] == '[') skip = MatchPair(k, stop, '[', ']');
            else if (ch == '<' && At(k, "<!--"))
            {
                skip = _s.IndexOf("-->", k + 4, stop - (k + 4), StringComparison.Ordinal);
                skip = skip < 0 ? stop : skip + 3;
            }
            else if (ch == '<') skip = RefEnd(k, stop);
            else if (ch == target) return k;

            k = skip > k ? skip : k + 1;
        }
        return -1;
    }

    private int RefEnd(int i, int end)
    {
        if (!AtIgnoreCase(i, "<ref") || i + 4 >= end) return -1;
        char after = _s[i + 4];
        if (after != '>' && after != '/' && !char.IsWhiteSpace(after)) return -1;
        int gt = _s.IndexOf('>', i + 4, end - (i + 4));
        if (gt < 0) return -1;
        if (_s[gt - 1] == '/') return gt + 1;
        int close = _s.IndexOf("</ref", gt + 1, end - (gt + 1), StringComparison.OrdinalIgnoreCase);
        if (close < 0) return -1;
        int gt2 = _s.IndexOf('>', close, end - close);
        return gt2 < 0 ? -1 : gt2 + 1;
    }

    private int TagEnd(int i, int end)
    {
        int k = i + 1;
        if (k < end && _s[k] == '/') k++;
        if (k >= end || !char.IsAsciiLetter(_s[k])) return -1;
        int limit = Math.Min(end, i + 400);
        for (; k < limit; k++)
        {
            if (_s[k] == '>') return k + 1;
            if (_s[k] == '<') return -1;
        }
        return -1;
    }

    private int ExternalLinkEnd(int i, int end)
    {
        int k = i + 1;
        if (!(AtIgnoreCase(k, "http://") || AtIgnoreCase(k, "https://") || AtIgnoreCase(k, "ftp://") || At(k, "//"))) return -1;
        for (; k < end && _s[k] != '\n'; k++)
        {
            if (_s[k] == ']') return k + 1;
        }
        return -1;
    }

    private int BareUrlEnd(int i, int end)
    {
        if (i > 0 && IsWordChar(_s[i - 1])) return -1;
        if (!(AtIgnoreCase(i, "http://") || AtIgnoreCase(i, "https://") || AtIgnoreCase(i, "ftp://"))) return -1;
        int k = i;
        while (k < end && !char.IsWhiteSpace(_s[k]) && "[]<>{}|\"".IndexOf(_s[k]) < 0) k++;
        while (k > i && ".,;:!?)".IndexOf(_s[k - 1]) >= 0) k--;
        return k;
    }

    private int EntityEnd(int i, int end)
    {
        int limit = Math.Min(end, i + 12);
        int k = i + 1;
        if (k < limit && _s[k] == '#') k++;
        int bodyStart = k;
        while (k < limit && char.IsAsciiLetterOrDigit(_s[k])) k++;
        return k > bodyStart && k < limit && _s[k] == ';' ? k + 1 : -1;
    }

    private int MagicWordEnd(int i, int end)
    {
        if (!At(i, "__")) return -1;
        int k = i + 2;
        while (k < end && (char.IsAsciiLetterUpper(_s[k]) || _s[k] is 'Ä' or 'Ö' or 'Å')) k++;
        return k > i + 2 && k + 1 < end && _s[k] == '_' && _s[k + 1] == '_' ? k + 2 : -1;
    }

    /// <summary>Line-initial markup: table syntax, list markers, redirects, horizontal rules.</summary>
    private int LineStartMarkupEnd(int i, int end)
    {
        int lineEnd = _s.IndexOf('\n', i, end - i);
        if (lineEnd < 0) lineEnd = end;
        if (At(i, "{|") || At(i, "|-")) return lineEnd;
        if (At(i, "|}") || At(i, "|+")) return i + 2;
        char c = _s[i];
        if (c is '|' or '!') return i + 1;
        foreach (var redirect in Redirects)
        {
            if (AtIgnoreCase(i, redirect)) return i + redirect.Length;
        }
        int k = i;
        if (c is '*' or '#' or ':' or ';')
        {
            while (k < end && _s[k] is '*' or '#' or ':' or ';') k++;
            return k;
        }
        if (At(i, "----"))
        {
            while (k < end && _s[k] == '-') k++;
            return k;
        }
        return i;
    }

    private static readonly string[] Redirects = ["#OHJAUS", "#UUDELLEENOHJAUS", "#REDIRECT"];

    private bool RestOfLineBlank(int j, int end)
    {
        while (j < end && _s[j] is ' ' or '\t') j++;
        return j >= end || _s[j] == '\n';
    }

    private bool InTableLine(int i)
    {
        int ls = i;
        while (ls > 0 && _s[ls - 1] != '\n') ls--;
        while (ls < i && _s[ls] is ' ' or '\t') ls++;
        return _s[ls] is '|' or '!';
    }

    internal static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) ||
        CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
}

/// <summary>Small string helpers shared by the tokenizer, the block splitter and the diff.</summary>
internal static class TextUtil
{
    private static readonly HashSet<string> CategoryPrefixes = new(StringComparer.OrdinalIgnoreCase) { "luokka", "category" };
    private static readonly HashSet<string> FilePrefixes = new(StringComparer.OrdinalIgnoreCase) { "tiedosto", "kuva", "file", "image", "media" };

    /// <summary>Category, File or Interwiki for a link target with that namespace prefix; Prose for an ordinary link.</summary>
    public static TokenClass LinkKind(ReadOnlySpan<char> target)
    {
        target = target.Trim();
        if (target.IsEmpty || target[0] == ':') return TokenClass.Prose;   // [[:Luokka:X]] links to the category page
        int colon = target.IndexOf(':');
        if (colon <= 0) return TokenClass.Prose;
        var prefix = target[..colon].Trim().ToString();
        if (CategoryPrefixes.Contains(prefix)) return TokenClass.Category;
        if (FilePrefixes.Contains(prefix)) return TokenClass.File;
        return IsLanguageCode(prefix) ? TokenClass.Interwiki : TokenClass.Prose;
    }

    /// <summary>Lower-case language code as written in interwiki links: en, sv, smn, zh-min-nan, simple. "YK:n" is not one.</summary>
    private static bool IsLanguageCode(string p)
    {
        if (p == "simple") return true;
        var parts = p.Split('-');
        if (parts[0].Length is < 2 or > 3 || parts[0] == "wp") return false;
        foreach (var part in parts)
        {
            if (part.Length == 0 || part.Any(ch => ch is < 'a' or > 'z')) return false;
        }
        return true;
    }

    /// <summary>Whitespace runs → one space, trimmed.</summary>
    public static string Collapse(ReadOnlySpan<char> s)
    {
        bool needs = false;
        for (int k = 0; k < s.Length && !needs; k++)
        {
            needs = char.IsWhiteSpace(s[k]) && (s[k] != ' ' || k == 0 || k == s.Length - 1 || char.IsWhiteSpace(s[k + 1]));
        }
        if (!needs) return s.ToString();

        var sb = new StringBuilder(s.Length);
        bool pendingSpace = false;
        foreach (char ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = sb.Length > 0;
                continue;
            }
            if (pendingSpace) sb.Append(' ');
            pendingSpace = false;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>Collapse, then drop empty "; " items left by template separators: "; a; ; b;" → "a; b".</summary>
    public static string CleanValues(string s)
    {
        var collapsed = Collapse(s);
        if (collapsed.IndexOf(';') < 0) return collapsed;
        var parts = collapsed.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return string.Join("; ", parts);
    }

    public static bool HasAlnum(string s)
    {
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch)) return true;
        }
        return false;
    }

    public static int AlnumCount(string s)
    {
        int n = 0;
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch)) n++;
        }
        return n;
    }
}
