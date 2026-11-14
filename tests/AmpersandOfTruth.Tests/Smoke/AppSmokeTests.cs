using System.Net;
using AmpersandOfTruth.Tests.Infrastructure;

namespace AmpersandOfTruth.Tests.Smoke;

/// <summary>Boots the real app in-process. Catches DI/routing/startup breakage that unit tests miss.</summary>
public class AppSmokeTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/analysis")]
    [InlineData("/investigate")]
    [InlineData("/investigate/winter-war")]
    [InlineData("/methodology")]
    [InlineData("/articles/1150")]
    [InlineData("/articles/1/at")]
    [InlineData("/investigate/winter-war/incidents/ww-01")]
    [InlineData("/accounts/Example")]
    [InlineData("/health")]
    public async Task Route_returns_success(string url)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/definitely-not-a-page")]
    [InlineData("/notes")]
    public async Task Unknown_route_returns_404(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
