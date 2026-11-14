using System.Net;
using System.Reflection;
using AmpersandOfTruth.Tests.Infrastructure;
using AmpersandOfTruth.Web.Features.Search;
using AmpersandOfTruth.Web.Features.Translation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace AmpersandOfTruth.Tests.Smoke;

/// <summary>
/// Boots the real app with Corpus, Ollama, and Gemini pointed at closed/empty endpoints (#52).
/// Every route must still return HTTP 200 with its expected fallback banner and never leak an exception or stack trace.
/// </summary>
public sealed class DegradedSmokeTests(DegradedSmokeTests.DegradedFixture fixture)
    : IClassFixture<DegradedSmokeTests.DegradedFixture>
{
    [Theory]
    [InlineData("/", "status-banner")]
    [InlineData("/investigate/winter-war", "status-banner")]
    [InlineData("/investigate/winter-war/incidents/ww-01", "status-banner")]
    [InlineData("/articles/1150", "article-error")]
    [InlineData("/accounts/Example", "status-banner")]
    [InlineData("/investigate", "sample-data-banner")]
    [InlineData("/methodology", "methodology-page")]
    [InlineData("/analysis", null)]
    public async Task Route_with_closed_dependencies_returns_200_expected_banner_and_no_exception_text(
        string url,
        string? expectedTestId)
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        if (expectedTestId is not null)
        {
            Assert.Contains(
                $"""
                data-testid="{expectedTestId}"
                """,
                html,
                StringComparison.Ordinal);
        }

        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
        Assert.DoesNotContain("at AmpersandOfTruth", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_endpoint_with_closed_dependencies_returns_200_degraded_without_stack_trace()
    {
        using var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", body.Trim());
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("at AmpersandOfTruth", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Per_user_services_are_scoped_and_feature_singletons_hold_no_per_circuit_services()
    {
        using var scopeA = fixture.Factory.Services.CreateScope();
        using var scopeB = fixture.Factory.Services.CreateScope();

        var recentA = scopeA.ServiceProvider.GetRequiredService<RecentTopics>();
        var recentB = scopeB.ServiceProvider.GetRequiredService<RecentTopics>();
        Assert.NotSame(recentA, recentB);
        Assert.Same(recentA, scopeA.ServiceProvider.GetRequiredService<RecentTopics>());

        var langA = scopeA.ServiceProvider.GetRequiredService<LanguagePreference>();
        var langB = scopeB.ServiceProvider.GetRequiredService<LanguagePreference>();
        Assert.NotSame(langA, langB);
        Assert.Same(langA, scopeA.ServiceProvider.GetRequiredService<LanguagePreference>());

        Type[] perCircuitTypes = [typeof(NavigationManager), typeof(IJSRuntime), typeof(RecentTopics), typeof(LanguagePreference)];

        foreach (var descriptor in fixture.Descriptors)
        {
            if (descriptor.Lifetime != ServiceLifetime.Singleton)
            {
                continue;
            }

            var implType = descriptor.ImplementationType ?? descriptor.ServiceType;
            if (implType.Namespace?.StartsWith("AmpersandOfTruth.Web.Features", StringComparison.Ordinal) != true)
            {
                continue;
            }

            Assert.DoesNotContain(implType, perCircuitTypes);

            if (!descriptor.ServiceType.IsGenericTypeDefinition)
            {
                var instance = fixture.Factory.Services.GetService(descriptor.ServiceType);
                if (instance is not null)
                {
                    foreach (var field in instance.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        Assert.False(
                            perCircuitTypes.Any(t => t.IsAssignableFrom(field.FieldType)),
                            $"Singleton {instance.GetType().FullName} holds per-circuit field {field.Name} ({field.FieldType.Name}).");
                    }
                }
            }
        }
    }

    public sealed class DegradedFixture : IAsyncLifetime
    {
        private readonly AppFactory _root = new();
        private IReadOnlyList<ServiceDescriptor> _descriptors = [];

        public WebApplicationFactory<Program> Factory { get; private set; } = default!;

        public IReadOnlyList<ServiceDescriptor> Descriptors => _descriptors;

        public Task InitializeAsync()
        {
            Factory = _root.WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "ConnectionStrings:Corpus",
                    "Server=127.0.0.1;Port=1;Database=corpus;User ID=corpus_ro;Password=none;Connection Timeout=1;Default Command Timeout=2");
                builder.UseSetting("Embedding:OllamaUrl", "http://127.0.0.1:1");
                builder.UseSetting("Embedding:FallbackOllamaUrl", "http://127.0.0.1:1");
                builder.UseSetting("Gemini:ApiKey", "");
                builder.ConfigureServices(services => _descriptors = services.ToList());
            });
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await Factory.DisposeAsync();
            await _root.DisposeAsync();
        }
    }
}
