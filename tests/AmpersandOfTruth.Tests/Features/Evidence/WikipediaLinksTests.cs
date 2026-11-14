namespace AmpersandOfTruth.Tests.Features.Evidence;

using AmpersandOfTruth.Web.Features.Evidence;
using Xunit;

public class WikipediaLinksTests
{
    [Fact]
    public void BaseUrl_is_fi_wikipedia()
    {
        Assert.Equal("https://fi.wikipedia.org", WikipediaLinks.BaseUrl);
    }

    [Theory]
    [InlineData(123, "https://fi.wikipedia.org/w/index.php?oldid=123")]
    [InlineData(9876543210, "https://fi.wikipedia.org/w/index.php?oldid=9876543210")]
    public void Revision_returns_correct_url(long revId, string expectedUrl)
    {
        var uri = WikipediaLinks.Revision(revId);

        Assert.Equal(expectedUrl, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public void Revision_throws_for_non_positive_revId(long revId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WikipediaLinks.Revision(revId));
    }

    [Theory]
    [InlineData(123, "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=123")]
    [InlineData(9876543210, "https://fi.wikipedia.org/w/index.php?diff=prev&oldid=9876543210")]
    public void Diff_returns_correct_url(long revId, string expectedUrl)
    {
        var uri = WikipediaLinks.Diff(revId);

        Assert.Equal(expectedUrl, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-999)]
    public void Diff_throws_for_non_positive_revId(long revId)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WikipediaLinks.Diff(revId));
    }

    [Theory]
    [InlineData("Alice", "https://fi.wikipedia.org/wiki/Special:Contributions/Alice")]
    [InlineData("Matti Meikäläinen", "https://fi.wikipedia.org/wiki/Special:Contributions/Matti_Meik%C3%A4l%C3%A4inen")]
    [InlineData("Väinämöinen", "https://fi.wikipedia.org/wiki/Special:Contributions/V%C3%A4in%C3%A4m%C3%B6inen")]
    [InlineData("~2025-26183-73", "https://fi.wikipedia.org/wiki/Special:Contributions/~2025-26183-73")]
    [InlineData("User/Subpage", "https://fi.wikipedia.org/wiki/Special:Contributions/User/Subpage")]
    [InlineData("User/Sub Page", "https://fi.wikipedia.org/wiki/Special:Contributions/User/Sub_Page")]
    [InlineData("User:Admin", "https://fi.wikipedia.org/wiki/Special:Contributions/User:Admin")]
    public void Contributions_returns_correct_url(string user, string expectedUrl)
    {
        var uri = WikipediaLinks.Contributions(user);

        Assert.Equal(expectedUrl, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t  ")]
    public void Contributions_throws_for_null_or_blank(string? user)
    {
        Assert.Throws<ArgumentException>(() => WikipediaLinks.Contributions(user!));
    }

    [Theory]
    [InlineData("Finland", "https://fi.wikipedia.org/wiki/Finland")]
    [InlineData("Tuntematon sotilas", "https://fi.wikipedia.org/wiki/Tuntematon_sotilas")]
    [InlineData("Suomen sisällissota", "https://fi.wikipedia.org/wiki/Suomen_sis%C3%A4llissota")]
    [InlineData("Helsinki/Alueet", "https://fi.wikipedia.org/wiki/Helsinki/Alueet")]
    [InlineData("Helsinki/Käpylä", "https://fi.wikipedia.org/wiki/Helsinki/K%C3%A4pyl%C3%A4")]
    [InlineData("Luokka:Suomen historia", "https://fi.wikipedia.org/wiki/Luokka:Suomen_historia")]
    [InlineData("Luokka:Käpylä/Aliluokka", "https://fi.wikipedia.org/wiki/Luokka:K%C3%A4pyl%C3%A4/Aliluokka")]
    public void Article_returns_correct_url(string title, string expectedUrl)
    {
        var uri = WikipediaLinks.Article(title);

        Assert.Equal(expectedUrl, uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t  ")]
    public void Article_throws_for_null_or_blank(string? title)
    {
        Assert.Throws<ArgumentException>(() => WikipediaLinks.Article(title!));
    }
}
