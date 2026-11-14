using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Components.Layout;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AmpersandOfTruth.Tests.Components.Layout;

public sealed class ThemeToggleTests : BunitContext
{
    [Fact]
    public void ThemeToggle_cycles_System_Light_Dark_System_and_invokes_js_module()
    {
        var module = JSInterop.SetupModule("./js/theme.js");
        module.Setup<string?>("get").SetResult("system");
        module.SetupVoid("set", _ => true).SetVoidResult();

        var cut = Render<ThemeToggle>();

        var btn = cut.Find("[data-testid=theme-toggle]");
        Assert.Equal("Theme: System", btn.GetAttribute("aria-label"));
        Assert.Equal("System", btn.QuerySelector(".aot-theme-toggle__label")?.TextContent);
        var svg = btn.QuerySelector("svg");
        Assert.NotNull(svg);
        Assert.Equal("16", svg.GetAttribute("width"));
        Assert.Equal("16", svg.GetAttribute("height"));

        btn.Click();
        btn = cut.Find("[data-testid=theme-toggle]");
        Assert.Equal("Theme: Light", btn.GetAttribute("aria-label"));
        Assert.Equal("Light", btn.QuerySelector(".aot-theme-toggle__label")?.TextContent);

        btn.Click();
        btn = cut.Find("[data-testid=theme-toggle]");
        Assert.Equal("Theme: Dark", btn.GetAttribute("aria-label"));
        Assert.Equal("Dark", btn.QuerySelector(".aot-theme-toggle__label")?.TextContent);

        btn.Click();
        btn = cut.Find("[data-testid=theme-toggle]");
        Assert.Equal("Theme: System", btn.GetAttribute("aria-label"));
        Assert.Equal("System", btn.QuerySelector(".aot-theme-toggle__label")?.TextContent);

        var setCalls = module.Invocations["set"];
        Assert.Equal(3, setCalls.Count);
        Assert.Equal("light", setCalls[0].Arguments[0]);
        Assert.Equal("dark", setCalls[1].Arguments[0]);
        Assert.Equal("system", setCalls[2].Arguments[0]);
    }

    [Fact]
    public void ThemeToggle_hydrates_initial_mode_from_js_module()
    {
        var module = JSInterop.SetupModule("./js/theme.js");
        module.Setup<string?>("get").SetResult("dark");
        module.SetupVoid("set", _ => true).SetVoidResult();

        var cut = Render<ThemeToggle>();

        cut.WaitForAssertion(() =>
        {
            var btn = cut.Find("[data-testid=theme-toggle]");
            Assert.Equal("Theme: Dark", btn.GetAttribute("aria-label"));
        });

        cut.Find("[data-testid=theme-toggle]").Click();
        Assert.Equal("Theme: System", cut.Find("[data-testid=theme-toggle]").GetAttribute("aria-label"));
        var call = Assert.Single(module.Invocations["set"]);
        Assert.Equal("system", call.Arguments[0]);
    }

    [Fact]
    public void ThemeToggle_when_js_import_throws_stays_System_and_does_not_throw()
    {
        Services.AddSingleton<IJSRuntime>(new ThrowingJsRuntime());

        var cut = Render<ThemeToggle>();

        var btn = cut.Find("[data-testid=theme-toggle]");
        Assert.Equal("Theme: System", btn.GetAttribute("aria-label"));

        btn.Click();
        Assert.Equal("Theme: System", cut.Find("[data-testid=theme-toggle]").GetAttribute("aria-label"));
    }

    [Fact]
    public void ThemeToggle_when_js_set_throws_stays_System_and_does_not_throw()
    {
        var module = JSInterop.SetupModule("./js/theme.js");
        module.Setup<string?>("get").SetResult("system");
        module.SetupVoid("set", _ => true).SetException(new JSException("localStorage blocked"));

        var cut = Render<ThemeToggle>();
        var btn = cut.Find("[data-testid=theme-toggle]");
        Assert.Equal("Theme: System", btn.GetAttribute("aria-label"));

        btn.Click();
        Assert.Equal("Theme: System", cut.Find("[data-testid=theme-toggle]").GetAttribute("aria-label"));
    }

    [Fact]
    public void TokensCss_prefers_color_scheme_dark_block_matches_data_theme_dark_custom_properties()
    {
        var repoRoot = FindRepoRoot();
        var tokensCss = File.ReadAllText(Path.Combine(repoRoot, "docs", "design", "tokens.css"));

        var darkBlockMatch = Regex.Match(
            tokensCss,
            """(?:^|\r?\n)\[data-theme="dark"\]\s*\{(?<body>[^}]*)\}""",
            RegexOptions.CultureInvariant);
        Assert.True(darkBlockMatch.Success, """Expected [data-theme="dark"] block in docs/design/tokens.css.""");

        var mediaBlockMatch = Regex.Match(
            tokensCss,
            """@media\s*\(\s*prefers-color-scheme:\s*dark\s*\)\s*\{\s*:root:not\(\[data-theme="light"\]\)\s*\{(?<body>[^}]*)\}\s*\}""",
            RegexOptions.CultureInvariant);
        Assert.True(
            mediaBlockMatch.Success,
            """Expected @media (prefers-color-scheme: dark) { :root:not([data-theme="light"]) { ... } } block in docs/design/tokens.css.""");

        var darkProps = ParseCustomProperties(darkBlockMatch.Groups["body"].Value);
        var mediaProps = ParseCustomProperties(mediaBlockMatch.Groups["body"].Value);

        Assert.NotEmpty(darkProps);
        foreach (var (name, value) in darkProps)
        {
            Assert.True(
                mediaProps.TryGetValue(name, out var mediaValue),
                $"""Custom property '{name}' from [data-theme="dark"] is missing in @media (prefers-color-scheme: dark).""");
            Assert.Equal(value, mediaValue);
        }

        Assert.Equal(darkProps.Count, mediaProps.Count);
    }

    private static Dictionary<string, string> ParseCustomProperties(string cssBody)
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(cssBody, @"(?<name>--[a-zA-Z0-9_-]+)\s*:\s*(?<value>[^;]+);"))
        {
            props[m.Groups["name"].Value] = m.Groups["value"].Value.Trim();
        }

        return props;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find repository root containing AmpersandOfTruth.sln");
    }

    private sealed class ThrowingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new JSException("JS unavailable");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new JSException("JS unavailable");
    }
}
