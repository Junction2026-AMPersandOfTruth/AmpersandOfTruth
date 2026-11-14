using System.Net;
using AmpersandOfTruth.Tests.Infrastructure;

namespace AmpersandOfTruth.Tests.Smoke;

/// <summary>Boots the real app in-process. Catches DI/routing/startup breakage that unit tests miss.</summary>
public class AppSmokeTests(AppFactory factory) : IClassFixture<AppFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/notes")]
    [InlineData("/analysis")]
    [InlineData("/health")]
    public async Task Route_returns_success(string url)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_route_returns_404()
    {
        var response = await factory.CreateClient().GetAsync("/definitely-not-a-page");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
