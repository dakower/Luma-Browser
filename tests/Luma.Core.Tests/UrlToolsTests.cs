using Luma.Core;
using Xunit;

namespace Luma.Core.Tests;

public sealed class UrlToolsTests
{
    [Theory]
    [InlineData("example.com", "https://example.com")]
    [InlineData("hello world", "https://www.google.com/search?q=hello%20world")]
    public void NormalizeInput_HandlesAddressAndSearch(string input, string expected) => Assert.Equal(expected, UrlTools.NormalizeInput(input, "google"));


    [Fact]
    public void NormalizeInput_UsesLumaSearchRoute()
        => Assert.Equal("luma://search?q=hello%20world&mode=all", UrlTools.NormalizeInput("hello world", "luma"));

    [Fact]
    public void CleanTrackingLink_RemovesTrackersButKeepsUsefulQuery()
        => Assert.Equal("https://example.com/page?id=42", UrlTools.CleanTrackingLink("https://example.com/page?id=42&utm_source=x&fbclid=y"));

    [Theory]
    [InlineData("https://www.example.com/a", "https://example.com/b", true)]
    [InlineData("https://example.com", "https://other.com", false)]
    public void SameSite_ComparesNormalizedHosts(string first, string second, bool expected) => Assert.Equal(expected, UrlTools.SameSite(first, second));

    [Fact]
    public void MatchScore_PrefersDomainPrefix() => Assert.True(UrlTools.MatchScore("you", "Video", "https://youtube.com") > UrlTools.MatchScore("you", "You are here", "https://example.com"));
}
