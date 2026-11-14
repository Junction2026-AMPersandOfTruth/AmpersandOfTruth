using System.Text;

namespace AmpersandOfTruth.Web.Features.WikiDiff;

/// <param name="Prose">Running text, one block per line group (blank line between blocks). No templates, infoboxes,
/// references, files, categories or interwiki links; link labels instead of link markup.</param>
/// <param name="References">The citation text of every <c>&lt;ref&gt;</c>, in order (empty string for a reused named ref).</param>
public sealed record WikitextProseView(string Prose, IReadOnlyList<string> References);

/// <summary>The light normaliser of #29: the prose view of a whole revision, with references kept apart.</summary>
public static class WikitextProse
{
    public static WikitextProseView Of(string wikitext)
    {
        ArgumentNullException.ThrowIfNull(wikitext);
        var text = wikitext.Replace("\r\n", "\n");
        var paragraphs = new List<string>();
        var references = new List<string>();
        var tokens = new List<WikiToken>();
        foreach (var block in WikitextBlocks.Split(text))
        {
            tokens.Clear();
            WikitextTokenizer.Tokenize(text, block.Start, block.End, block.Index, tokens);
            var sb = new StringBuilder();
            foreach (var t in tokens)
            {
                if (t.Class == TokenClass.Reference) references.Add(t.Prose);
                else if (t.Class is TokenClass.Prose or TokenClass.Markup) sb.Append(t.Prose);
            }
            var prose = TextUtil.Collapse(sb.ToString());
            if (prose.Length > 0) paragraphs.Add(prose);
        }
        return new WikitextProseView(string.Join("\n\n", paragraphs), references);
    }
}
