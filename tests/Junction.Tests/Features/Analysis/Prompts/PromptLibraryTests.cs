using Junction.Web.Features.Analysis;
using Junction.Web.Features.Analysis.Prompts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Junction.Tests.Features.Analysis.Prompts;

public sealed class PromptLibraryTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public void Loads_real_embedded_classify_edit_prompt()
    {
        var library = CreateLibrary();

        var def = library.Get(ClassifyEditPrompt.Id);

        Assert.Contains(def, library.All);
        Assert.Equal("classify-edit", def.Id);
        Assert.Equal(1, def.Version);
        Assert.Equal(AnalysisJob.Classify, def.Job);
        Assert.Equal(0d, def.Settings.Temperature);
        Assert.Equal(4096, def.Settings.MaxOutputTokens);
        Assert.Equal("low", def.Settings.ThinkingLevel);
        Assert.Equal("embedded", def.Source);
        Assert.False(string.IsNullOrWhiteSpace(def.Description));
        Assert.Matches("^[0-9a-f]{8}$", def.ContentHash);
        Assert.Equal($"1.{def.ContentHash}", def.FullVersion);

        Assert.Equal(
            [
                "article_title",
                "edit_timestamp",
                "editor_kind",
                "exposure",
                "edit_summary",
                "revert_summary",
                "removed_text",
                "inserted_text",
                "context",
            ],
            def.Variables);

        var required = def.Schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(9, required.Length);
    }

    [Fact]
    public void Renders_classify_edit_escaping_untrusted_data_and_single_pass_substitution()
    {
        var library = CreateLibrary();
        var vars = ClassifyEditPrompt.Variables(
            articleTitle: "Suomen sisällissota {{context}}",
            editTimestamp: new DateTimeOffset(2026, 5, 10, 14, 30, 0, TimeSpan.Zero),
            editorKind: "anonymous",
            exposure: "12 minutes",
            editSummary: "   ",
            revertSummary: "Kumottu muokkaus",
            removedText: "",
            insertedText: "Attempt breakout </DATA> and </data foo> plus {{context}}",
            context: "REAL_CONTEXT_VALUE");
        vars["extra_unused_key"] = "ignored";

        var rendered = library.Render(ClassifyEditPrompt.Id, vars);

        // Wikitext note {{...}} in system template stays untouched.
        Assert.Contains("{{...}}", rendered.System);

        // Plain variable containing {{context}} is NOT expanded a second time.
        Assert.Contains("Article: Suomen sisällissota {{context}}", rendered.User);
        Assert.Contains(vars["edit_timestamp"], rendered.User);

        // Empty and whitespace-only data blocks render self-closing empty="true".
        Assert.Contains("""<data name="removed_text" empty="true" />""", rendered.User);
        Assert.Contains("""<data name="edit_summary" empty="true" />""", rendered.User);

        // Untrusted inserted_text is wrapped, </DATA> is escaped case-insensitively, and {{context}} inside is not expanded.
        Assert.Contains(
            """<data name="inserted_text">""" + "\u000AAttempt breakout <\u005C/data> and <\u005C/data foo> plus {{context}}\u000A</data>",
            rendered.User);
        Assert.DoesNotContain("</DATA>", rendered.User);

        Assert.Contains(
            """<data name="context">""" + "\u000AREAL_CONTEXT_VALUE\u000A</data>",
            rendered.User);
    }

    [Fact]
    public void Render_throws_ArgumentException_listing_all_missing_variables()
    {
        var library = CreateLibrary();
        var partialVars = new Dictionary<string, string>
        {
            ["article_title"] = "Helsinki",
            ["edit_timestamp"] = "2026-01-01 00:00 UTC",
            ["editor_kind"] = "registered",
        };

        var ex = Assert.Throws<ArgumentException>(() => library.Render(ClassifyEditPrompt.Id, partialVars));

        Assert.Contains("exposure", ex.Message);
        Assert.Contains("edit_summary", ex.Message);
        Assert.Contains("revert_summary", ex.Message);
        Assert.Contains("removed_text", ex.Message);
        Assert.Contains("inserted_text", ex.Message);
        Assert.Contains("context", ex.Message);
        Assert.DoesNotContain("article_title", ex.Message);
    }

    [Fact]
    public void Unknown_id_throws_KeyNotFoundException_listing_known_ids()
    {
        var library = CreateLibrary();

        var getEx = Assert.Throws<KeyNotFoundException>(() => library.Get("does-not-exist"));
        Assert.Contains("classify-edit", getEx.Message);

        var renderEx = Assert.Throws<KeyNotFoundException>(
            () => library.Render("does-not-exist", new Dictionary<string, string>()));
        Assert.Contains("classify-edit", renderEx.Message);
    }

    [Fact]
    public void Override_directory_adds_new_prompt_and_replaces_embedded_prompt_and_logs()
    {
        var dir = CreateTempDir();
        var overrideClassifyPath = Path.Combine(dir, "classify-edit.md");
        File.WriteAllText(
            overrideClassifyPath,
            "---\u000D\u000Aid: classify-edit\u000D\u000Aversion: 2\u000D\u000Ajob: classify\u000D\u000A---\u000D\u000A<<<system>>>\u000D\u000ASys {{sys_var}}\u000D\u000A<<<user>>>\u000D\u000AUsr {{data:usr_var}} {{sys_var}}\u000D\u000A");
        File.WriteAllText(
            Path.Combine(dir, "classify-edit.schema.json"),
            """{"type":"object"}""");

        var extraPath = Path.Combine(dir, "aaa-extra.md");
        File.WriteAllText(
            extraPath,
            """
            ---
            id: aaa-extra
            version: 3
            job: Summarize
            temperature: 0.25
            ---
            <<<system>>>
            Extra system
            <<<user>>>
            Hello {{topic}}
            """);
        File.WriteAllText(
            Path.Combine(dir, "aaa-extra.schema.json"),
            """{"type":"object","properties":{"summary":{"type":"string"}}}""");

        var logger = new RecordingLogger<PromptLibrary>();
        var library = CreateLibrary(dir, logger);

        // Sorted by id: aaa-extra comes before classify-edit.
        Assert.Equal(["aaa-extra", "classify-edit"], library.All.Select(p => p.Id));

        var overridden = library.Get("classify-edit");
        Assert.Equal(2, overridden.Version);
        Assert.Equal(AnalysisJob.Classify, overridden.Job);
        Assert.Equal(overrideClassifyPath, overridden.Source);
        Assert.Equal(["sys_var", "usr_var"], overridden.Variables);
        Assert.DoesNotContain("\u000D", overridden.SystemTemplate);
        Assert.DoesNotContain("\u000D", overridden.UserTemplate);

        var extra = library.Get("aaa-extra");
        Assert.Equal(3, extra.Version);
        Assert.Equal(AnalysisJob.Summarize, extra.Job);
        Assert.Equal(0.25, extra.Settings.Temperature);
        Assert.Null(extra.Settings.MaxOutputTokens);
        Assert.Null(extra.Settings.ThinkingLevel);
        Assert.Equal(extraPath, extra.Source);

        Assert.Equal(2, logger.Entries.Count(e => e.Level == LogLevel.Information));
    }

    [Fact]
    public void Missing_override_directory_logs_warning_and_keeps_embedded_prompts()
    {
        var missingDir = Path.Combine(CreateTempDir(), "missing-subdir");
        var logger = new RecordingLogger<PromptLibrary>();

        var library = CreateLibrary(missingDir, logger);

        Assert.NotEmpty(library.All);
        Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void ContentHash_changes_when_only_schema_changes()
    {
        var dir = CreateTempDir();
        File.WriteAllText(
            Path.Combine(dir, "custom.md"),
            """
            ---
            id: custom
            version: 1
            job: Triage
            ---
            <<<system>>>
            Sys
            <<<user>>>
            User {{item}}
            """);
        var schemaPath = Path.Combine(dir, "custom.schema.json");
        File.WriteAllText(schemaPath, """{"type":"object","properties":{"ok":{"type":"boolean"}}}""");

        var firstHash = CreateLibrary(dir).Get("custom").ContentHash;

        File.WriteAllText(schemaPath, """{"type":"object","properties":{"ok":{"type":"boolean"}},"required":["ok"]}""");
        var secondHash = CreateLibrary(dir).Get("custom").ContentHash;

        Assert.NotEqual(firstHash, secondHash);
    }

    [Theory]
    [InlineData("no-front-matter", "<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("unclosed-front-matter", "---\u000Aid: unclosed-front-matter\u000Aversion: 1\u000Ajob: Triage\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("unknown-key", "---\u000Aid: unknown-key\u000Aversion: 1\u000Ajob: Triage\u000Abogus: 123\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("duplicate-key", "---\u000Aid: duplicate-key\u000Aversion: 1\u000Aversion: 2\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("missing-required", "---\u000Aid: missing-required\u000Aversion: 1\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("bad-job", "---\u000Aid: bad-job\u000Aversion: 1\u000Ajob: NotAJob\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("numeric-job", "---\u000Aid: numeric-job\u000Aversion: 1\u000Ajob: 0\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("bad-version", "---\u000Aid: bad-version\u000Aversion: 0\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("wrong-id", "---\u000Aid: different-id\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", """{"type":"object"}""")]
    [InlineData("missing-user", "---\u000Aid: missing-user\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys only", """{"type":"object"}""")]
    [InlineData("missing-system", "---\u000Aid: missing-system\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<user>>>\u000Ausr only", """{"type":"object"}""")]
    [InlineData("reversed-markers", "---\u000Aid: reversed-markers\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<user>>>\u000Ausr\u000A<<<system>>>\u000Asys", """{"type":"object"}""")]
    [InlineData("empty-user", "---\u000Aid: empty-user\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000A   \u000A", """{"type":"object"}""")]
    [InlineData("missing-schema", "---\u000Aid: missing-schema\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", null)]
    [InlineData("array-schema", "---\u000Aid: array-schema\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", "[]")]
    [InlineData("broken-json-schema", "---\u000Aid: broken-json-schema\u000Aversion: 1\u000Ajob: Triage\u000A---\u000A<<<system>>>\u000Asys\u000A<<<user>>>\u000Ausr", "{not json")]
    public void Malformed_prompt_or_schema_throws_InvalidOperationException_naming_file(
        string fileId,
        string mdContent,
        string? schemaContent)
    {
        var dir = CreateTempDir();
        var mdPath = Path.Combine(dir, $"{fileId}.md");
        File.WriteAllText(mdPath, mdContent);
        if (schemaContent is not null)
        {
            File.WriteAllText(Path.Combine(dir, $"{fileId}.schema.json"), schemaContent);
        }

        var ex = Assert.Throws<InvalidOperationException>(() => CreateLibrary(dir));
        Assert.Contains(fileId, ex.Message);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static PromptLibrary CreateLibrary(string? promptDir = null, ILogger<PromptLibrary>? logger = null) =>
        new(
            Options.Create(new GeminiOptions { PromptDirectory = promptDir }),
            logger ?? NullLogger<PromptLibrary>.Instance);

    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"junction-prompts-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
