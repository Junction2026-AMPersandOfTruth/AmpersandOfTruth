namespace AmpersandOfTruth.Tests.Features.Evidence;

using AmpersandOfTruth.Web.Features.Evidence;
using Xunit;

public class EditorDisplayTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   \t  ")]
    public void Null_empty_or_whitespace_is_classified_as_Hidden(string? input)
    {
        var label = EditorDisplay.For(input);

        Assert.Equal(EditorKind.Hidden, label.Kind);
        Assert.Equal("Hidden editor", label.DisplayName);
        Assert.Null(label.Qualifier);
        Assert.Null(label.ProfileUrl);
        Assert.Equal("hidden", label.GroupingKey);
        Assert.False(label.IsAnonymous);
        Assert.Equal("Hidden editor", label.PlainText);
    }

    [Theory]
    [InlineData("~2025-26183-73")]
    [InlineData("~2026-54975-0")]
    [InlineData("~2025-34728-96")]
    [InlineData("  ~2025-26183-73  ")]
    public void Temporary_account_regex_matches_and_classifies_correctly(string input)
    {
        var trimmed = input.Trim();
        var label = EditorDisplay.For(input);

        Assert.Equal(EditorKind.Temporary, label.Kind);
        Assert.Equal(trimmed, label.DisplayName);
        Assert.Equal("temporary account", label.Qualifier);
        Assert.NotNull(label.ProfileUrl);
        Assert.Equal($"https://fi.wikipedia.org/wiki/Special:Contributions/{trimmed}", label.ProfileUrl.AbsoluteUri);
        Assert.Equal($"temp:{trimmed}", label.GroupingKey);
        Assert.True(label.IsAnonymous);
        Assert.Equal($"{trimmed} (temporary account)", label.PlainText);
    }

    [Theory]
    [InlineData("85.76.123.45", "85.76.x.x", "ip4:85.76")]
    [InlineData("  85.76.123.45  ", "85.76.x.x", "ip4:85.76")]
    [InlineData("192.168.1.1", "192.168.x.x", "ip4:192.168")]
    [InlineData("10.0.0.1", "10.0.x.x", "ip4:10.0")]
    public void Valid_IPv4_is_classified_and_masked(string input, string expectedDisplay, string expectedGrouping)
    {
        var label = EditorDisplay.For(input);

        Assert.Equal(EditorKind.IPv4, label.Kind);
        Assert.Equal(expectedDisplay, label.DisplayName);
        Assert.Equal("anonymous", label.Qualifier);
        Assert.Null(label.ProfileUrl);
        Assert.Equal(expectedGrouping, label.GroupingKey);
        Assert.True(label.IsAnonymous);
        Assert.Equal($"{expectedDisplay} (anonymous)", label.PlainText);
    }

    [Theory]
    [InlineData("2001:14BB:51:D813:A1B9:8F12:23B6:8D13", "2001:14BB:…", "ip6:2001:14BB")]
    [InlineData("2001:db8::1", "2001:DB8:…", "ip6:2001:DB8")]
    [InlineData("2001:0db8::1", "2001:DB8:…", "ip6:2001:DB8")]
    [InlineData("2a02:2788:1:2:3:4:5:6", "2A02:2788:…", "ip6:2A02:2788")]
    [InlineData("::1", "0:0:…", "ip6:0:0")]
    [InlineData("fe80::1", "FE80:0:…", "ip6:FE80:0")]
    public void Valid_IPv6_is_classified_and_masked(string input, string expectedDisplay, string expectedGrouping)
    {
        var label = EditorDisplay.For(input);

        Assert.Equal(EditorKind.IPv6, label.Kind);
        Assert.Equal(expectedDisplay, label.DisplayName);
        Assert.Equal("anonymous", label.Qualifier);
        Assert.Null(label.ProfileUrl);
        Assert.Equal(expectedGrouping, label.GroupingKey);
        Assert.True(label.IsAnonymous);
        Assert.Equal($"{expectedDisplay} (anonymous)", label.PlainText);
    }

    [Theory]
    [InlineData("~Tilde", "user:~Tilde", "https://fi.wikipedia.org/wiki/Special:Contributions/~Tilde")]
    [InlineData("999.1.1.1", "user:999.1.1.1", "https://fi.wikipedia.org/wiki/Special:Contributions/999.1.1.1")]
    [InlineData("Matti Meikäläinen", "user:Matti Meikäläinen", "https://fi.wikipedia.org/wiki/Special:Contributions/Matti_Meik%C3%A4l%C3%A4inen")]
    [InlineData("  Matti Meikäläinen  ", "user:Matti Meikäläinen", "https://fi.wikipedia.org/wiki/Special:Contributions/Matti_Meik%C3%A4l%C3%A4inen")]
    [InlineData("Not:An:IPv6:Address", "user:Not:An:IPv6:Address", "https://fi.wikipedia.org/wiki/Special:Contributions/Not:An:IPv6:Address")]
    public void Other_inputs_are_classified_as_Named(string input, string expectedGrouping, string expectedProfile)
    {
        var trimmed = input.Trim();
        var label = EditorDisplay.For(input);

        Assert.Equal(EditorKind.Named, label.Kind);
        Assert.Equal(trimmed, label.DisplayName);
        Assert.Null(label.Qualifier);
        Assert.NotNull(label.ProfileUrl);
        Assert.Equal(expectedProfile, label.ProfileUrl.AbsoluteUri);
        Assert.Equal(expectedGrouping, label.GroupingKey);
        Assert.False(label.IsAnonymous);
        Assert.Equal(trimmed, label.PlainText);
    }

    [Fact]
    public void IPv4_and_IPv6_do_not_leak_full_address_or_tail_and_have_no_profile_url()
    {
        var ipv4Label = EditorDisplay.For("85.76.123.45");
        Assert.Null(ipv4Label.ProfileUrl);
        var ipv4Str = ipv4Label.ToString();
        Assert.DoesNotContain("85.76.123.45", ipv4Str);
        Assert.DoesNotContain("123.45", ipv4Str);

        var ipv6Label = EditorDisplay.For("2001:14BB:51:D813:A1B9:8F12:23B6:8D13");
        Assert.Null(ipv6Label.ProfileUrl);
        var ipv6Str = ipv6Label.ToString();
        Assert.DoesNotContain("2001:14BB:51:D813:A1B9:8F12:23B6:8D13", ipv6Str);
        Assert.DoesNotContain("A1B9:8F12", ipv6Str);
        Assert.DoesNotContain("51:D813", ipv6Str);
        Assert.DoesNotContain("23B6:8D13", ipv6Str);
    }

    [Fact]
    public void GroupingKey_matches_across_same_range_and_differs_for_others()
    {
        var ip1 = EditorDisplay.For("85.76.1.2");
        var ip2 = EditorDisplay.For("85.76.200.9");
        var ipDifferent = EditorDisplay.For("85.77.1.2");

        Assert.Equal(ip1.GroupingKey, ip2.GroupingKey);
        Assert.NotEqual(ip1.GroupingKey, ipDifferent.GroupingKey);

        var user1 = EditorDisplay.For("Alice");
        var user2 = EditorDisplay.For("Bob");
        var user1Again = EditorDisplay.For(" Alice ");

        Assert.NotEqual(user1.GroupingKey, user2.GroupingKey);
        Assert.Equal(user1.GroupingKey, user1Again.GroupingKey);

        var ipv6First = EditorDisplay.For("2001:14BB:51:D813::1");
        var ipv6Second = EditorDisplay.For("2001:14bb:ffff:0000::2");
        var ipv6Different = EditorDisplay.For("2001:14BC:51:D813::1");

        Assert.Equal(ipv6First.GroupingKey, ipv6Second.GroupingKey);
        Assert.NotEqual(ipv6First.GroupingKey, ipv6Different.GroupingKey);

        var hidden1 = EditorDisplay.For(null);
        var hidden2 = EditorDisplay.For("   ");
        Assert.Equal(hidden1.GroupingKey, hidden2.GroupingKey);
    }

    [Fact]
    public void PlainText_and_IsAnonymous_for_every_kind()
    {
        var hidden = EditorDisplay.For(null);
        Assert.Equal("Hidden editor", hidden.PlainText);
        Assert.False(hidden.IsAnonymous);

        var temp = EditorDisplay.For("~2025-26183-73");
        Assert.Equal("~2025-26183-73 (temporary account)", temp.PlainText);
        Assert.True(temp.IsAnonymous);

        var ip4 = EditorDisplay.For("85.76.123.45");
        Assert.Equal("85.76.x.x (anonymous)", ip4.PlainText);
        Assert.True(ip4.IsAnonymous);

        var ip6 = EditorDisplay.For("2001:14BB:51:D813:A1B9:8F12:23B6:8D13");
        Assert.Equal("2001:14BB:… (anonymous)", ip6.PlainText);
        Assert.True(ip6.IsAnonymous);

        var named = EditorDisplay.For("Matti Meikäläinen");
        Assert.Equal("Matti Meikäläinen", named.PlainText);
        Assert.False(named.IsAnonymous);
    }

    [Theory]
    [InlineData(
        "Käyttäjän [[Special:Contributions/85.76.123.45|85.76.123.45]] muokkaukset kumottiin ja sivu palautettiin käyttäjän [[User:Matti|Matti]] tekemään versioon.",
        "Käyttäjän 85.76.x.x muokkaukset kumottiin ja sivu palautettiin käyttäjän Matti tekemään versioon.")]
    [InlineData(
        "Kumottu muokkaus [[Special:Diff/23181977|23181977]], jonka teki [[Special:Contributions/2001:14BB:51:D813:A1B9:8F12:23B6:8D13|2001:14BB:51:D813:A1B9:8F12:23B6:8D13]] ([[User talk:2001:14BB:51:D813:A1B9:8F12:23B6:8D13|keskustelu]])",
        "Kumottu muokkaus 23181977, jonka teki 2001:14BB:… (keskustelu)")]
    [InlineData("[[Toiminnot:Muokkaukset/10.0.0.1|10.0.0.1]]", "10.0.x.x")]
    [InlineData("85.76.1.2.", "85.76.x.x.")]
    [InlineData("2001:db8::1:", "2001:DB8:…:")]
    [InlineData("fe80::1", "FE80:0:…")]
    [InlineData("[[User talk:2001:db8::1]]", "User talk:2001:DB8:…")]
    [InlineData("Special:Contributions/2001:db8::1", "Special:Contributions/2001:DB8:…")]
    [InlineData("klo 12:30:45", "klo 12:30:45")]
    [InlineData("999.1.1.1", "999.1.1.1")]
    [InlineData("[[Talvisota]]", "Talvisota")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void MaskComment_flattens_wikilinks_and_masks_ips(string? comment, string expected)
    {
        var result = EditorDisplay.MaskComment(comment);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void MaskComment_never_leaks_raw_ip_or_tail_or_wikilink_delimiters()
    {
        var input6 = "Kumottu muokkaus [[Special:Diff/23181977|23181977]], jonka teki [[Special:Contributions/2001:14BB:51:D813:A1B9:8F12:23B6:8D13|2001:14BB:51:D813:A1B9:8F12:23B6:8D13]] ([[User talk:2001:14BB:51:D813:A1B9:8F12:23B6:8D13|keskustelu]])";
        var masked6 = EditorDisplay.MaskComment(input6);
        Assert.DoesNotContain("2001:14BB:51:D813:A1B9:8F12:23B6:8D13", masked6);
        Assert.DoesNotContain("23B6:8D13", masked6);
        Assert.DoesNotContain("[[", masked6);
        Assert.DoesNotContain("]]", masked6);
        Assert.Contains("2001:14BB:…", masked6);
        Assert.Contains("23181977", masked6);
        Assert.Contains("keskustelu", masked6);

        var input4 = "Käyttäjän [[Special:Contributions/85.76.123.45|85.76.123.45]] muokkaukset kumottiin ja sivu palautettiin käyttäjän [[User:Matti|Matti]] tekemään versioon.";
        var masked4 = EditorDisplay.MaskComment(input4);
        Assert.DoesNotContain("85.76.123.45", masked4);
        Assert.DoesNotContain("123.45", masked4);
        Assert.DoesNotContain("[[", masked4);
        Assert.DoesNotContain("]]", masked4);
        Assert.Contains("85.76.x.x", masked4);
        Assert.Contains("Matti", masked4);
    }
}
