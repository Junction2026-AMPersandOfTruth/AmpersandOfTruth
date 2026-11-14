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
    [InlineData("/library")]
    [InlineData("/hotspots")]
    [InlineData("/library/00000000-0000-0000-0000-000000000000/report")]
    [InlineData("/r/doesnotexist")]
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
    [InlineData("/library/00000000-0000-0000-0000-000000000000/report.md")]
    public async Task Unknown_route_returns_404(string url)
    {
        var response = await factory.CreateClient().GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
