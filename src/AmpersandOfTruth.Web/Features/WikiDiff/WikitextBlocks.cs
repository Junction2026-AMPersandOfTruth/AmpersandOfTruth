namespace AmpersandOfTruth.Web.Features.WikiDiff;

/// <summary>A paragraph-level unit: [Start, End) in the revision text, and its whitespace-normalised text as the diff key.</summary>
internal sealed record WikiBlock(int Index, int Start, int End, bool IsHeading, string Key);

/// <summary>
/// Splits wikitext into blocks: a paragraph (consecutive text lines), a heading, a list item, or a structural block
/// (a template, table, comment, file, category or interwiki line). A construct that spans lines (an infobox, a table,
/// a multi-line <c>&lt;ref&gt;</c>) stays inside one block, even across blank lines. A <c>&lt;ref&gt;</c> that is never
/// closed (<see cref="RefTags"/>) is not such a construct: the blocks after it are split as usual.
/// </summary>
internal static class WikitextBlocks
{
    /// <summary>An unbalanced construct (a stray "{{") stops swallowing lines at the first blank line after this many.</summary>
    private const int MaxOpenLines = 300;

    private enum LineKind { Text, Heading, Single }

    public static List<WikiBlock> Split(string s)
    {
        var blocks = new List<WikiBlock>();
        var depth = new Depth();
        int start = -1, end = -1, lines = 0;
        bool single = false, heading = false;

        void Close()
        {
            if (start < 0) return;
            while (end > start && char.IsWhiteSpace(s[end - 1])) end--;
            if (end > start) blocks.Add(new WikiBlock(blocks.Count, start, end, heading, TextUtil.Collapse(s.AsSpan(start, end - start))));
            start = -1;
        }

        int pos = 0;
        while (true)
        {
            int nl = s.IndexOf('\n', pos);
            int le = nl < 0 ? s.Length : nl;
            var line = s.AsSpan(pos, le - pos);
            bool blank = line.IsWhiteSpace();

            if (start >= 0 && depth.Open)
            {
                if (blank && lines > MaxOpenLines)
                {
                    depth = new Depth();
                    Close();
                }
                else
                {
                    depth.Feed(s, pos, le);
                    end = le;
                    lines++;
                    if (single && !depth.Open) Close();
                }
            }
            else if (blank)
            {
                Close();
            }
            else
            {
                var kind = Classify(line);
                if (kind != LineKind.Text || single) Close();
                if (start < 0)
                {
                    start = pos;
                    lines = 0;
                    single = kind != LineKind.Text;
                    heading = kind == LineKind.Heading;
                }
                depth.Feed(s, pos, le);
                end = le;
                lines++;
                if (single && !depth.Open) Close();
            }

            if (nl < 0) break;
            pos = nl + 1;
        }
        Close();
        return blocks;
    }

    private static LineKind Classify(ReadOnlySpan<char> line)
    {
        var trimmed = line.TrimEnd();
        if (trimmed.Length >= 3 && trimmed[0] == '=' && trimmed[^1] == '=') return LineKind.Heading;
        if (line[0] is '*' or '#' or ':' or ';') return LineKind.Single;
        if (line.StartsWith("{{") || line.StartsWith("{|") || line.StartsWith("<!--") || line.StartsWith("__") || line.StartsWith("----"))
            return LineKind.Single;
        if (line.StartsWith("[["))
        {
            var inner = line[2..];
            int stop = inner.IndexOfAny('|', ']');
            if (TextUtil.LinkKind(stop < 0 ? inner : inner[..stop]) != TokenClass.Prose) return LineKind.Single;
        }
        return LineKind.Text;
    }

    /// <summary>Open multi-line constructs at the end of the lines fed so far.</summary>
    private struct Depth
    {
        private int _templates, _tables;
        private bool _comment;

        // A reference is open up to the end of its closing tag (an absolute offset), and only if it has one: an
        // unclosed <ref> (RefTags) opens nothing, so the paragraphs and headings after it stay blocks of their own.
        private int _refEnd, _lineEnd;
        private bool _noTagEnd;

        public readonly bool Open => _templates > 0 || _tables > 0 || _comment || _refEnd > _lineEnd;

        /// <param name="s">The whole revision; the line is [<paramref name="start"/>, <paramref name="end"/>).</param>
        public void Feed(string s, int start, int end)
        {
            _lineEnd = end;
            var line = s.AsSpan(start, end - start);
            var t = line.TrimStart();
            if (!_comment && t.StartsWith("{|")) _tables++;
            else if (!_comment && t.StartsWith("|}") && _tables > 0) _tables--;

            int k = 0;
            while (k < line.Length)
            {
                if (_comment)
                {
                    int close = line[k..].IndexOf("-->", StringComparison.Ordinal);
                    if (close < 0) return;
                    _comment = false;
                    k += close + 3;
                    continue;
                }
                int next = line[k..].IndexOfAny('<', '{', '}');
                if (next < 0) return;
                k += next;
                var rest = line[k..];
                if (rest.StartsWith("<!--", StringComparison.Ordinal))
                {
                    _comment = true;
                    k += 4;
                }
                else if (rest.StartsWith("{{", StringComparison.Ordinal))
                {
                    _templates++;
                    k += 2;
                }
                else if (rest.StartsWith("}}", StringComparison.Ordinal))
                {
                    if (_templates > 0) _templates--;
                    k += 2;
                }
                else if (RefTags.OpensAt(s, start + k, s.Length))
                {
                    OpenRef(s, start + k);
                    k += 4;
                }
                else
                {
                    k++;
                }
            }
        }

        private void OpenRef(string s, int at)
        {
            if (at < _refEnd || _noTagEnd) return;   // a <ref name=… /> reuse inside a reference that is already open
            int gt = s.IndexOf('>', at + 4);
            if (gt < 0)
            {
                _noTagEnd = true;   // no '>' left in the text: no later tag can end either, so stop looking
                return;
            }
            if (s[gt - 1] == '/')
            {
                _refEnd = gt + 1;   // self-closing; open only if the tag itself runs over a line break
                return;
            }
            int close = RefTags.CloseOf(s, gt + 1, s.Length, out _);
            if (close > 0) _refEnd = close;
        }
    }
}
