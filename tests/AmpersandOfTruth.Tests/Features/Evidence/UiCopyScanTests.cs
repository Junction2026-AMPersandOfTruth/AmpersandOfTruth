namespace AmpersandOfTruth.Tests.Features.Evidence;

using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Evidence;
using Xunit;

public sealed class UiCopyScanTests
{
    internal static string ExtractCopy(string razor)
    {
        if (string.IsNullOrEmpty(razor))
        {
            return string.Empty;
        }

        // 1. Remove @* … *@ comments
        var noRazorComments = Regex.Replace(razor, @"@\*[\s\S]*?\*@", string.Empty);

        // 2. Remove <!-- … --> comments
        var noHtmlComments = Regex.Replace(noRazorComments, @"<!--[\s\S]*?-->", string.Empty);

        // 3. Remove everything from the first line whose trimmed text starts with @code to the end of the file
        var lines = noHtmlComments.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        var keptLines = new List<string>();
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("@code", StringComparison.Ordinal))
            {
                break;
            }
            keptLines.Add(line);
        }

        return string.Join("\n", keptLines);
    }

    [Fact]
    public void ExtractCopy_drops_code_blocks_and_both_comment_styles_but_keeps_markup_text()
    {
        var razor = """
            @page "/test"
            <!-- HTML comment mentioning troll -->
            <h1>Safe Heading</h1>
            @* Razor comment mentioning liar *@
            <p>Visible body text</p>
            @code {
                private string secret = "disinformation agent";
                void DoSomething() { }
            }
            """;

        var copy = ExtractCopy(razor);

        Assert.Contains("Safe Heading", copy);
        Assert.Contains("Visible body text", copy);

        Assert.DoesNotContain("HTML comment", copy);
        Assert.DoesNotContain("Razor comment", copy);
        Assert.DoesNotContain("troll", copy);
        Assert.DoesNotContain("liar", copy);
        Assert.DoesNotContain("@code", copy);
        Assert.DoesNotContain("disinformation agent", copy);
        Assert.DoesNotContain("DoSomething", copy);
    }

    [Fact]
    public void ExtractCopy_preserves_content_when_no_code_block()
    {
        var razor = """
            <div>
                <span>Only Markup</span>
            </div>
            """;

        var copy = ExtractCopy(razor);
        Assert.Contains("Only Markup", copy);
    }

    [Fact]
    public void ExtractCopy_handles_indented_code_line()
    {
        var razor = """
            <p>Hello</p>
                @code{
                int x = 1;
            }
            """;

        var copy = ExtractCopy(razor);
        Assert.Contains("Hello", copy);
        Assert.DoesNotContain("int x", copy);
    }

    [Fact]
    public void Razor_copy_scan_finds_no_violations()
    {
        var repoRoot = FindRepoRoot();
        var webDir = Path.Combine(repoRoot, "src", "AmpersandOfTruth.Web");
        Assert.True(Directory.Exists(webDir), $"Web directory not found at: {webDir}");

        var razorFiles = Directory.EnumerateFiles(webDir, "*.razor", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(webDir, path).Replace('\\', '/');
                return !relative.StartsWith("Features/Analysis/", StringComparison.OrdinalIgnoreCase) &&
                       !relative.StartsWith("Features/Notes/", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        Assert.NotEmpty(razorFiles);

        var failures = ScanCopy(repoRoot, razorFiles);

        Assert.True(
            failures.Count == 0,
            $"Found {failures.Count} copy violation(s) in Razor files:\n{string.Join("\n", failures)}");
    }

    /// <summary>
    /// docs/design is the source of truth for the UI (its README §5): pages copy their sentences from the
    /// previews, so a banned term in a mock-up is a banned term in the app one PR later.
    /// Comments are skipped; attributes (title, placeholder, aria-label) and inline chart scripts are scanned.
    /// Jargon is off, as for Razor, so class names such as aot-tier don't trip it.
    /// </summary>
    [Fact]
    public void Design_preview_copy_scan_finds_no_violations()
    {
        var repoRoot = FindRepoRoot();
        var componentsDir = Path.Combine(repoRoot, "docs", "design", "components");
        Assert.True(Directory.Exists(componentsDir), $"Design components directory not found at: {componentsDir}");

        var previews = Directory.EnumerateFiles(componentsDir, "preview.html", SearchOption.AllDirectories).ToList();

        Assert.NotEmpty(previews);

        var failures = ScanCopy(repoRoot, previews);

        Assert.True(
            failures.Count == 0,
            $"Found {failures.Count} copy violation(s) in design previews:\n{string.Join("\n", failures)}");
    }

    private static List<string> ScanCopy(string repoRoot, IEnumerable<string> files)
    {
        var failures = new List<string>();

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            var copy = ExtractCopy(content);
            var violations = CopyRules.FindViolations(copy, includeJargon: false);

            foreach (var v in violations)
            {
                var relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
                failures.Add($"{relPath}: {v.Rule.Term} (\"{v.Match}\")");
            }
        }

        return failures;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root containing AmpersandOfTruth.sln");
    }
}
