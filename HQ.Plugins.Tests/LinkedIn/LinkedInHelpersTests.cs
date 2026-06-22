using System.Text.Json;
using HQ.Plugins.LinkedIn;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInHelpersTests
{
    // ---- CsrfFromCookie ----

    [Theory]
    [InlineData("JSESSIONID=\"ajax:1234567890\"", "ajax:1234567890")]
    [InlineData("li_at=abc; JSESSIONID=\"ajax:99\"; lang=en", "ajax:99")]
    [InlineData("JSESSIONID=ajax:nopaquotes", "ajax:nopaquotes")]
    [InlineData("li_at=only", "")]
    [InlineData("", "")]
    public void CsrfFromCookie_ExtractsJSessionId(string cookie, string expected)
        => Assert.Equal(expected, LinkedInBrowser.CsrfFromCookie(cookie));

    // ---- LinkedInPaths.SanitizeAccount ----

    [Theory]
    [InlineData("Primary", "primary")]
    [InlineData("  My Account  ", "my-account")]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("", "default")]
    [InlineData("!!!", "default")]
    public void SanitizeAccount_ProducesSafeSegment(string input, string expected)
        => Assert.Equal(expected, LinkedInPaths.SanitizeAccount(input));

    [Fact]
    public void ProfileDir_IsUnderDataDirAndAccount()
    {
        var dir = LinkedInPaths.ProfileDir("Primary");
        Assert.Contains("primary", dir);
        Assert.EndsWith("profile", dir);
    }

    // ---- RateLimitGate ----

    [Fact]
    public void RateLimitGate_ConsumesUpToCapThenBlocks()
    {
        var gate = new RateLimitGate();
        Assert.True(gate.TryConsume(RateLimitCategory.Invitation, 2));
        Assert.True(gate.TryConsume(RateLimitCategory.Invitation, 2));
        Assert.False(gate.TryConsume(RateLimitCategory.Invitation, 2));
        Assert.Equal(2, gate.Count(RateLimitCategory.Invitation));
    }

    [Fact]
    public void RateLimitGate_TracksCategoriesIndependently()
    {
        var gate = new RateLimitGate();
        Assert.True(gate.TryConsume(RateLimitCategory.Message, 1));
        Assert.False(gate.TryConsume(RateLimitCategory.Message, 1));
        Assert.True(gate.TryConsume(RateLimitCategory.Search, 1)); // separate bucket
    }

    [Fact]
    public void RateLimitGate_ResetsOnNewUtcDay()
    {
        var day = new DateTime(2026, 6, 22, 10, 0, 0, DateTimeKind.Utc);
        var gate = new RateLimitGate(() => day);
        Assert.True(gate.TryConsume(RateLimitCategory.Search, 1));
        Assert.False(gate.TryConsume(RateLimitCategory.Search, 1));

        day = day.AddDays(1); // clock advances a day
        Assert.True(gate.TryConsume(RateLimitCategory.Search, 1));
    }

    [Fact]
    public void RateLimitGate_ZeroCapAlwaysBlocks()
        => Assert.False(new RateLimitGate().TryConsume(RateLimitCategory.Search, 0));

    // ---- LinkedInParsing ----

    [Fact]
    public void SummarizeProfile_PullsNestedProfileFields()
    {
        var json = JsonDocument.Parse("""
            {"profile":{"firstName":"Ada","lastName":"Lovelace","headline":"Engineer","locationName":"London","publicIdentifier":"ada"}}
            """).RootElement;

        var summary = LinkedInParsing.SummarizeProfile(json);

        Assert.Equal("Ada", summary["firstName"]);
        Assert.Equal("Lovelace", summary["lastName"]);
        Assert.Equal("Engineer", summary["headline"]);
        Assert.Equal("London", summary["location"]);
        Assert.Equal("ada", summary["publicIdentifier"]);
    }

    [Fact]
    public void SummarizeProfile_ToleratesMissingFields()
    {
        var json = JsonDocument.Parse("""{"unexpected":true}""").RootElement;
        var summary = LinkedInParsing.SummarizeProfile(json);
        Assert.Empty(summary);
    }

    [Fact]
    public void SummarizeCompany_UnwrapsElementsArray()
    {
        var json = JsonDocument.Parse("""
            {"elements":[{"name":"Anthropic","universalName":"anthropic","staffCount":500}]}
            """).RootElement;

        var summary = LinkedInParsing.SummarizeCompany(json);

        Assert.Equal("Anthropic", summary["name"]);
        Assert.Equal("anthropic", summary["universalName"]);
        Assert.Equal("500", summary["staffCount"]);
    }

    [Fact]
    public void SummarizeHits_HandlesStringAndObjectTitles()
    {
        var json = JsonDocument.Parse("""
            {"elements":[
              {"title":"Grace Hopper","subtext":"Rear Admiral","targetUrn":"urn:li:fs_miniProfile:1"},
              {"title":{"text":"Acme Inc"},"subtitle":{"text":"Software"}}
            ]}
            """).RootElement;

        var hits = LinkedInParsing.SummarizeHits(json);

        Assert.Equal(2, hits.Count);
        Assert.Equal("Grace Hopper", hits[0]["title"]);
        Assert.Equal("Rear Admiral", hits[0]["subtitle"]);
        Assert.Equal("urn:li:fs_miniProfile:1", hits[0]["urn"]);
        Assert.Equal("Acme Inc", hits[1]["title"]);
    }

    [Fact]
    public void SummarizeHits_EmptyOnNonObject()
        => Assert.Empty(LinkedInParsing.SummarizeHits(JsonDocument.Parse("null").RootElement));
}
