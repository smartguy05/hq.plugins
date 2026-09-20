using System.Reflection;
using System.Text.Json;
using HQ.Models.Attributes;
using HQ.Plugins.LinkedIn;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInHelpersTests
{
    // ---- WP6A-7 third pass: the production tool-call path (LinkedInCommand.DoWork -> GetBrowser)
    // now resolves a real, host-injected caller org id and scopes the profile directory by it,
    // the same as the interactive login path -- so the tooltip's earlier "distinct orgs MUST use
    // a distinct AccountLabel or they'll collide" warning is no longer an operator obligation.
    [Fact]
    public void AccountLabelTooltip_DescribesOrganizationScopingOnBothPaths()
    {
        var property = typeof(ServiceConfig).GetProperty(nameof(ServiceConfig.AccountLabel));
        var tooltip = property!.GetCustomAttribute<TooltipAttribute>();

        Assert.NotNull(tooltip);
        Assert.Contains("organization", tooltip!.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("WP6A-7", tooltip.Text);
        // The old hard requirement to pick a distinct label per org is gone now that both paths
        // scope by the real org id automatically.
        Assert.DoesNotContain("MUST use a distinct AccountLabel", tooltip.Text);
    }

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
        var dir = LinkedInPaths.ProfileDir(Guid.Empty, "Primary");
        Assert.Contains("primary", dir);
        Assert.EndsWith("profile", dir);
    }

    [Fact]
    public void ProfileDir_UnresolvedOrgMatchesPreWP6A7Shape()
    {
        // Guid.Empty (no caller org available, e.g. the production per-agent path) must resolve
        // to the exact same path as before org-keying existed, so existing single-tenant/dev
        // deployments and already-authenticated profiles keep working unchanged.
        var dir = LinkedInPaths.ProfileDir(Guid.Empty, "Primary");
        Assert.DoesNotContain("org-", dir);
        Assert.EndsWith(Path.Combine("primary", "profile"), dir);
    }

    [Fact]
    public void ProfileDir_DifferentOrgsWithSameAccountLabelDoNotCollide()
    {
        // WP6A-7: two tenants that both leave AccountLabel at its "default" default must not
        // resolve to the same on-disk profile.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        var dirA = LinkedInPaths.ProfileDir(orgA, "default");
        var dirB = LinkedInPaths.ProfileDir(orgB, "default");

        Assert.NotEqual(dirA, dirB);
        Assert.Contains(orgA.ToString("N"), dirA);
        Assert.Contains(orgB.ToString("N"), dirB);
    }

    // ---- WP6A-7 re-review blocking #1: profile-ownership marker ----
    // Since the third pass, both the login flow and LinkedInCommand.GetBrowser resolve a real,
    // caller-scoped org id and key ProfileDir by it, so two different orgs no longer even share a
    // physical directory. IsProfileOwnedByAnotherOrg/ReadProfileOwner/WriteProfileOwner remain the
    // guard for the shared, unscoped bucket a tenancy-disabled deployment (Guid.Empty) still uses,
    // and for defense in depth against a manually copied/restored profile directory.

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", null, false)] // no prior owner -> allowed
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000", false)] // owner unscoped -> allowed
    [InlineData("00000000-0000-0000-0000-000000000000", "11111111-1111-1111-1111-111111111111", false)] // caller unscoped (tenancy disabled) -> allowed
    [InlineData("11111111-1111-1111-1111-111111111111", "11111111-1111-1111-1111-111111111111", false)] // same org re-authenticating -> allowed
    [InlineData("22222222-2222-2222-2222-222222222222", "11111111-1111-1111-1111-111111111111", true)] // different real orgs -> blocked
    public void IsProfileOwnedByAnotherOrg_OnlyBlocksTwoDifferentRealOrgs(
        string callerOrgText, string ownerOrgText, bool expectedBlocked)
    {
        var callerOrgId = Guid.Parse(callerOrgText);
        Guid? ownerOrgId = ownerOrgText is null ? null : Guid.Parse(ownerOrgText);

        Assert.Equal(expectedBlocked, LinkedInPaths.IsProfileOwnedByAnotherOrg(callerOrgId, ownerOrgId));
    }

    [Fact]
    public void ProfileOwner_RoundTripsThroughMarkerFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-owner-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(LinkedInPaths.ReadProfileOwner(dir)); // nothing written yet

            var orgId = Guid.NewGuid();
            LinkedInPaths.WriteProfileOwner(dir, orgId);

            Assert.Equal(orgId, LinkedInPaths.ReadProfileOwner(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ProfileOwner_ReadIsResilientToGarbageMarkerContent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-owner-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(LinkedInPaths.OwnerMarkerPath(dir), "not-a-guid");

            Assert.Null(LinkedInPaths.ReadProfileOwner(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
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
