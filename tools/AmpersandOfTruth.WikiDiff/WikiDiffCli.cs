using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.WikiDiff;

/// <summary>
/// <c>wikidiff</c> (#29): the C# wikitext diff engine for experiments in any language. Wrapper: <c>scripts/wikidiff.sh</c>.
/// <code>
///   wikidiff batch &lt; pairs.jsonl &gt; diffs.jsonl          one {"id","old","new"} per line → one result per line, same order
///   wikidiff one --new b.txt [--old a.txt] [--compact]    one diff of two files, pretty JSON (no --old = first revision)
///   wikidiff prose &lt; page.txt                            the prose view and references of one revision
/// </code>
/// JSON goes to stdout, the timing summary to stderr. Exit code 0; 1 if input cannot be read; 2 for usage errors.
/// </summary>
public static class WikiDiffCli
{
    private const string Usage = """
        usage:
          wikidiff batch < pairs.jsonl > diffs.jsonl       {"id": …, "old": "<wikitext>" | null, "new": "<wikitext>"} per line
          wikidiff one --new b.txt [--old a.txt] [--compact]
          wikidiff prose < page.txt
        Output contract: research/eval/CONTRACT.md §9.
        """;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            return args.FirstOrDefault() switch
            {
                "batch" when args.Length == 1 => Batch(),
                "one" => One(args[1..]),
                "prose" when args.Length == 1 => Prose(),
                "-h" or "--help" or "help" => Help(0),
                _ => Help(2),
            };
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"wikidiff: cannot read input: {ex.Message}");
            return 1;
        }
    }

    private static int Help(int code)
    {
        Console.Error.WriteLine(Usage);
        return code;
    }

    private static int Batch()
    {
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false), bufferSize: 1 << 16);
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), bufferSize: 1 << 16) { AutoFlush = false };
        var clock = Stopwatch.StartNew();
        int n = WikiDiffJsonl.Run(stdin, stdout);
        Console.Error.WriteLine($"wikidiff: {n} diff(s) in {clock.Elapsed.TotalMilliseconds:F0} ms");
        return 0;
    }

    private static int One(string[] args)
    {
        string? oldPath = null, newPath = null;
        bool compact = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--old" when i + 1 < args.Length: oldPath = args[++i]; break;
                case "--new" when i + 1 < args.Length: newPath = args[++i]; break;
                case "--compact": compact = true; break;
                default: return Help(2);
            }
        }
        if (newPath is null) return Help(2);

        var oldText = oldPath is null ? null : File.ReadAllText(oldPath, Encoding.UTF8);
        var newText = File.ReadAllText(newPath, Encoding.UTF8);
        var clock = Stopwatch.StartNew();
        var result = WikitextDiff.Compare(oldText, newText);
        Console.Error.WriteLine($"wikidiff: {clock.Elapsed.TotalMilliseconds:F1} ms, {result.Segments.Count} segment(s)");
        var id = JsonSerializer.SerializeToElement(Path.GetFileName(newPath));
        Console.WriteLine(WikiDiffJsonl.Serialize(id, result, indented: !compact));
        return 0;
    }

    private static int Prose()
    {
        var text = Console.In.ReadToEnd();
        var view = WikitextProse.Of(text);
        Console.WriteLine(JsonSerializer.Serialize(view, WikiDiffJsonl.IndentedOptions));
        return 0;
    }
}
