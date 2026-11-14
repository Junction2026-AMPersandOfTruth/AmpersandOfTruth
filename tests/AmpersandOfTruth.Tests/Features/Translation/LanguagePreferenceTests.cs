using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Analysis;
using AmpersandOfTruth.Web.Features.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AmpersandOfTruth.Tests.Features.Translation;

public sealed class LanguagePreferenceTests
{
    [Fact]
    public void Default_language_is_en()
    {
        var pref = new LanguagePreference();

        Assert.Equal("en", pref.Lang);
    }

    [Fact]
    public async Task LoadAsync_when_js_throws_keeps_en_and_does_not_throw()
    {
        var pref = new LanguagePreference();
        var raised = false;
        pref.Changed += () => raised = true;

        await pref.LoadAsync(new ThrowingJsRuntime());

        Assert.Equal("en", pref.Lang);
        Assert.False(raised);
    }

    [Fact]
    public async Task LoadAsync_reads_stored_fi_and_raises_Changed()
    {
        var js = new InMemoryJsRuntime { [LanguagePreference.StorageKey] = "fi" };
        var pref = new LanguagePreference();
        var raised = 0;
        pref.Changed += () => raised++;

        await pref.LoadAsync(js);

        Assert.Equal("fi", pref.Lang);
        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("en")]
    [InlineData("sv")]
    [InlineData("")]
    public async Task LoadAsync_unknown_or_null_values_keep_default_en(string? stored)
    {
        var js = new InMemoryJsRuntime();
        if (stored is not null)
        {
            js[LanguagePreference.StorageKey] = stored;
        }

        var pref = new LanguagePreference();
        await pref.LoadAsync(js);

        Assert.Equal("en", pref.Lang);
    }

    [Fact]
    public async Task SetAsync_persists_aot_lang_in_localStorage_and_raises_Changed()
    {
        var js = new InMemoryJsRuntime();
        var pref = new LanguagePreference();
        var observed = new List<string>();
        pref.Changed += () => observed.Add(pref.Lang);

        await pref.SetAsync(js, "fi");

        Assert.Equal("fi", pref.Lang);
        Assert.Equal("fi", js[LanguagePreference.StorageKey]);
        Assert.Equal(["fi"], observed);

        await pref.SetAsync(js, "en");

        Assert.Equal("en", pref.Lang);
        Assert.Equal("en", js[LanguagePreference.StorageKey]);
        Assert.Equal(["fi", "en"], observed);
    }

    [Fact]
    public async Task SetAsync_when_js_throws_still_updates_lang_and_raises_Changed()
    {
        var pref = new LanguagePreference();
        var raised = false;
        pref.Changed += () => raised = true;

        await pref.SetAsync(new ThrowingJsRuntime(), "fi");

        Assert.Equal("fi", pref.Lang);
        Assert.True(raised);
    }

    [Fact]
    public void AddTranslationFeature_registers_singleton_translator_and_scoped_language_preference()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAnalysisModel>(new FakeAnalysisModel());
        services.AddTranslationFeature();

        using var provider = services.BuildServiceProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var t1 = scope1.ServiceProvider.GetRequiredService<IEvidenceTranslator>();
        var t2 = scope2.ServiceProvider.GetRequiredService<IEvidenceTranslator>();
        Assert.IsType<GeminiEvidenceTranslator>(t1);
        Assert.Same(t1, t2);

        var p1a = scope1.ServiceProvider.GetRequiredService<LanguagePreference>();
        var p1b = scope1.ServiceProvider.GetRequiredService<LanguagePreference>();
        var p2 = scope2.ServiceProvider.GetRequiredService<LanguagePreference>();
        Assert.Same(p1a, p1b);
        Assert.NotSame(p1a, p2);
    }

    private sealed class ThrowingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("JavaScript interop calls cannot be issued during static prerendering.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException("JavaScript interop calls cannot be issued during static prerendering.");
    }

    private sealed class InMemoryJsRuntime : IJSRuntime
    {
        private readonly Dictionary<string, string> _storage = new(StringComparer.Ordinal);

        public string? this[string key]
        {
            get => _storage.GetValueOrDefault(key);
            set
            {
                if (value is null) _storage.Remove(key);
                else _storage[key] = value;
            }
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            switch (identifier)
            {
                case "localStorage.getItem":
                {
                    var key = (string)args![0]!;
                    object? value = _storage.GetValueOrDefault(key);
                    return new ValueTask<TValue>((TValue)value!);
                }
                case "localStorage.setItem":
                {
                    var key = (string)args![0]!;
                    var val = (string)args[1]!;
                    _storage[key] = val;
                    return new ValueTask<TValue>(default(TValue)!);
                }
                default:
                    throw new NotSupportedException($"Unexpected JS call: {identifier}");
            }
        }
    }
}
