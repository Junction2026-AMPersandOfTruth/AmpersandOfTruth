namespace AmpersandOfTruth.Web.Features.WikiDiff;

/// <summary>
/// Where a <c>&lt;ref …&gt;</c> ends. One rule for <see cref="WikitextBlocks"/> and <see cref="WikitextTokenizer"/>, so the
/// two always agree on what is inside a reference.
/// <para>
/// A reference is closed by the first <c>&lt;/ref&gt;</c> after its opening tag, unless another <c>&lt;ref …&gt;</c> opens or
/// a section heading starts before that. In that case the tag was left unclosed (a deleted or mistyped
/// <c>&lt;/ref&gt;</c>): the opening tag is plain markup and the text after it is ordinary text.
/// </para>
/// <para>
/// Without this, an unclosed reference runs on to the closing tag of the <em>next</em> reference and takes every
/// paragraph in between with it. The diff then reports one "added" reference holding a whole section of text the
/// edit never touched (S7 report §6.1: thirteen unrelated accounts on Talvisota were linked by that swallowed section).
/// </para>
/// </summary>
internal static class RefTags
{
    private const string Open = "<ref";
    private const string Close = "</ref";

    /// <summary>Is there a <c>&lt;ref&gt;</c>, <c>&lt;ref name=…&gt;</c> or <c>&lt;ref … /&gt;</c> tag at <paramref name="i"/>?
    /// <c>&lt;references&gt;</c> is not one.</summary>
    public static bool OpensAt(string s, int i, int end)
    {
        if (i + Open.Length >= end || !s.AsSpan(i, Open.Length).Equals(Open, StringComparison.OrdinalIgnoreCase)) return false;
        char after = s[i + Open.Length];
        return after is '>' or '/' || char.IsWhiteSpace(after);
    }

    /// <summary>
    /// The end (index after the <c>&gt;</c>) of the <c>&lt;/ref&gt;</c> that closes the reference whose body starts at
    /// <paramref name="bodyStart"/>, or -1 when the reference is unclosed within [<paramref name="bodyStart"/>,
    /// <paramref name="end"/>). <paramref name="scannedTo"/> is how far the search read, for the tokenizer's scan budget:
    /// it stops at the first closing tag, opening tag or heading, so a page of unclosed tags is read once, not once per tag.
    /// </summary>
    public static int CloseOf(string s, int bodyStart, int end, out int scannedTo)
    {
        int k = bodyStart;
        while (k < end)
        {
            int next = s.AsSpan(k, end - k).IndexOfAny('<', '\n');
            if (next < 0) break;
            k += next;

            if (s[k] == '\n')
            {
                k++;
                if (HeadingAt(s, k, end))
                {
                    scannedTo = k;
                    return -1;
                }
                continue;
            }

            if (ClosesAt(s, k, end, out int closeEnd))
            {
                scannedTo = closeEnd;
                return closeEnd;
            }
            if (OpensAt(s, k, end))
            {
                int gt = s.IndexOf('>', k + Open.Length, end - (k + Open.Length));
                if (gt < 0 || s[gt - 1] != '/')
                {
                    // Another reference opens first. (No '>' at all also means no closing tag is left to find.)
                    scannedTo = gt < 0 ? end : gt + 1;
                    return -1;
                }
                k = gt + 1;   // <ref name="x" /> inside the body: a reuse, not a new reference
                continue;
            }
            k++;
        }
        scannedTo = end;
        return -1;
    }

    /// <summary><c>&lt;/ref&gt;</c>, optionally with blanks before the <c>&gt;</c>. Not <c>&lt;/references&gt;</c>.</summary>
    private static bool ClosesAt(string s, int i, int end, out int closeEnd)
    {
        closeEnd = -1;
        if (i + Close.Length >= end || !s.AsSpan(i, Close.Length).Equals(Close, StringComparison.OrdinalIgnoreCase)) return false;
        int k = i + Close.Length;
        while (k < end && char.IsWhiteSpace(s[k])) k++;
        if (k >= end || s[k] != '>') return false;
        closeEnd = k + 1;
        return true;
    }

    /// <summary>Does a section heading (<c>== Tausta ==</c>, any level from 2) start at <paramref name="lineStart"/>?</summary>
    private static bool HeadingAt(string s, int lineStart, int end)
    {
        if (lineStart + 1 >= end || s[lineStart] != '=' || s[lineStart + 1] != '=') return false;
        int lineEnd = s.IndexOf('\n', lineStart, end - lineStart);
        if (lineEnd < 0) lineEnd = end;
        while (lineEnd > lineStart && char.IsWhiteSpace(s[lineEnd - 1])) lineEnd--;
        return lineEnd - lineStart >= 5 && s[lineEnd - 1] == '=' && s[lineEnd - 2] == '=';
    }
}
