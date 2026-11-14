using System.Text.Json;
using AmpersandOfTruth.Web.Features.Search;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace AmpersandOfTruth.Tests.Features.Search;

public sealed class RecentTopicsTests
{
    [Fact]
    public async Task AddAsync_dedupes_by_slug_orders_most_recent_first_and_caps_at_six()
    {
        var js = new InMemoryStorageJsRuntime();
        var recent = new RecentTopics(js);

        await recent.AddAsync("Civil War 1918");
        await recent.AddAsync("Winter War");
        await recent.AddAsync("Continuation War");
        await recent.AddAsync("Mannerheim");
        await recent.AddAsync("Mainila shots");
        await recent.AddAsync("Ceded Karelia");
        await recent.AddAsync("Finlandization");
        await recent.AddAsync("  winter war ");

        Assert.Equal(6, recent.Items.Count);
        Assert.Equal(
            ["winter-war", "finlandization", "ceded-karelia", "mainila-shots", "mannerheim", "continuation-war"],
            recent.Items.Select(x => x.Slug));
        Assert.Equal("winter war", recent.Items[0].Label);

        var storedJson = js.Data[RecentTopics.StorageKey];
        using var doc = JsonDocument.Parse(storedJson);
        Assert.Equal(6, doc.RootElement.GetArrayLength());
        Assert.Equal("winter-war", doc.RootElement[0].GetProperty("slug").GetString());
    }

    [Fact]
    public async Task LoadAsync_and_AddAsync_swallow_js_exceptions_and_corrupt_json()
    {
        var throwing = new RecentTopics(new ThrowingJsRuntime());
        await throwing.LoadAsync();
        Assert.Empty(throwing.Items);

        await throwing.AddAsync("Winter War");
        Assert.Single(throwing.Items);
        Assert.Equal("winter-war", throwing.Items[0].Slug);

        var corruptJs = new InMemoryStorageJsRuntime
        {
            Data = { [RecentTopics.StorageKey] = "{not-json" }
        };
        var corrupt = new RecentTopics(corruptJs);
        await corrupt.LoadAsync();
        Assert.Empty(corrupt.Items);
    }

    [Fact]
    public void Appsettings_Search_section_binds_seven_suggested_topics_without_duplicating_defaults()
    {
        var repoRoot = FindRepoRoot();
        var appSettingsPath = Path.Combine(repoRoot, "src", "AmpersandOfTruth.Web", "appsettings.json");

        var config = new ConfigurationBuilder()
            .AddJsonFile(appSettingsPath, optional: false)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddSearchFeature();

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<SearchOptions>>().Value;

        Assert.Equal(
            [
                "Civil War 1918",
                "Winter War",
                "Continuation War",
                "Mannerheim",
                "Mainila shots",
                "Ceded Karelia",
                "Finlandization"
            ],
            options.SuggestedTopics.Select(t => t.Label));
        Assert.All(options.SuggestedTopics, t => Assert.Equal(t.Label, t.Topic));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AmpersandOfTruth.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    private sealed class ThrowingJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            throw new InvalidOperationException("JS unavailable during prerendering.");

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new InvalidOperationException("JS unavailable during prerendering.");
    }

    private sealed class InMemoryStorageJsRuntime : IJSRuntime
    {
        public Dictionary<string, string> Data { get; } = new(StringComparer.Ordinal);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return identifier switch
            {
                "localStorage.getItem" => new ValueTask<TValue>((TValue)(object?)Data.GetValueOrDefault((string)args![0]!)!),
                "localStorage.setItem" => Set((string)args![0]!, (string)args[1]!),
                _ => throw new NotSupportedException(identifier),
            };

            ValueTask<TValue> Set(string key, string value)
            {
                Data[key] = value;
                return new ValueTask<TValue>(default(TValue)!);
            }
        }
    }
}
