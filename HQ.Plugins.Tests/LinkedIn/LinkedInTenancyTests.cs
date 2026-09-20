using System.Reflection;
using HQ.Models.Interfaces;
using HQ.Plugins.LinkedIn;
using HQ.Plugins.LinkedIn.Endpoints;
using HQ.Plugins.LinkedIn.Models;
using Microsoft.AspNetCore.Http;

namespace HQ.Plugins.Tests.LinkedIn;

/// <summary>
/// WP6A-7 (LinkedIn cross-tenant login/browser isolation) and WP6A-11 (x11vnc -localhost).
/// These tests exercise the pure resolver/keying logic directly rather than standing up a real
/// ASP.NET host or spawning Xvfb/x11vnc — see the P2 cluster ruling: only the host's
/// MapPluginHttpRoutes auth-policy wiring (owned by C12) needs a WebApplicationFactory-style
/// test; the plugin-side fix is a set of pure functions this exercises directly.
///
/// Shares the "LinkedIn command static state" collection with <see cref="LinkedInCommandTests"/>:
/// the <c>ResolveConfig_*</c> tests below read the process-wide
/// <see cref="LinkedInCommand.LastConfig"/> static (via <see cref="LinkedInLoginEndpoints.ResolveConfig"/>),
/// which <c>LinkedInCommandTests.DoWork_*</c> tests write as a documented side effect of calling
/// the real <c>DoWork</c>. Without this shared, non-parallel collection the two classes could run
/// concurrently on separate threads and race on that static (see
/// <see cref="LinkedInStaticStateCollection"/> for the full history). The constructor/Dispose
/// reset additionally makes each test in this class hermetic regardless of ordering.
/// </summary>
[Collection("LinkedIn command static state")]
public class LinkedInTenancyTests : IDisposable
{
    public LinkedInTenancyTests() => LinkedInCommand.ResetForTests();

    public void Dispose() => LinkedInCommand.ResetForTests();

    // ---- LinkedInLoginEndpoints.ResolveCallerOrgId ----

    [Fact]
    public void ResolveCallerOrgId_ParsesValidGuidHeader()
    {
        var orgId = Guid.NewGuid();
        Assert.Equal(orgId, LinkedInLoginEndpoints.ResolveCallerOrgId(orgId.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("'; DROP TABLE orgs;--")]
    public void ResolveCallerOrgId_FallsBackToEmptyOnMissingOrInvalidHeader(string header)
        => Assert.Equal(Guid.Empty, LinkedInLoginEndpoints.ResolveCallerOrgId(header));

    // ---- LinkedInLoginEndpoints.ResolveConfig ----

    [Fact]
    public void ResolveConfig_QueryStringAccountOverrideIsInert()
    {
        var orgId = Guid.NewGuid();
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Organization-Id"] = orgId.ToString();
        // WP6A-7: a ?account= override must no longer exist as an input to this resolver at all
        // (the query string is never read for the account any more) — simulate a caller still
        // trying it to document that it's inert.
        ctx.Request.QueryString = new QueryString("?account=someone-elses-account");

        var (config, resolvedOrgId) = LinkedInLoginEndpoints.ResolveConfig(ctx);

        Assert.Equal(orgId, resolvedOrgId);
        Assert.DoesNotContain("someone-elses-account", config.AccountLabel);
    }

    [Fact]
    public void ResolveConfig_NeverOverwritesAccountLabelWithAnOrgDerivedString()
    {
        // Regression (adversarial review, blocking #2): ResolveConfig used to overwrite the
        // real, human-configured AccountLabel with LinkedInPaths.SanitizeOrg(orgId) (e.g.
        // "org-<hex>"). LinkedInPaths.ProfileDir combines (orgId, accountLabel), so that made the
        // login flow write its authenticated Chromium profile to a directory
        // (DataDir()/org-<hex>/org-<hex>/profile) that LinkedInCommand.DoWork's production
        // tool-call path — which always resolves under Guid.Empty, see WP6A-7 deferred notes —
        // could never find (DataDir()/<real-account-label>/profile). AccountLabel must stay
        // whatever the ambient config says, regardless of which org is calling.
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Organization-Id"] = Guid.NewGuid().ToString();

        var (config, orgId) = LinkedInLoginEndpoints.ResolveConfig(ctx);

        Assert.NotEqual(Guid.Empty, orgId);
        Assert.DoesNotContain("org-", config.AccountLabel);
        Assert.Equal("default", config.AccountLabel); // ambient ServiceConfig default, untouched
    }

    [Fact]
    public void ResolveConfig_DifferentCallerOrgsResolveToDifferentOrgIds_ButTheSameAmbientAccountLabel()
    {
        // The two orgs are still isolated — via the orgId component of
        // LinkedInCommand.CacheKey / LinkedInLoginSession's session tracking — even though both
        // resolve to the same (ambient, not caller-controlled) AccountLabel.
        var ctxA = new DefaultHttpContext();
        ctxA.Request.Headers["X-Organization-Id"] = Guid.NewGuid().ToString();
        var ctxB = new DefaultHttpContext();
        ctxB.Request.Headers["X-Organization-Id"] = Guid.NewGuid().ToString();

        var (configA, orgA) = LinkedInLoginEndpoints.ResolveConfig(ctxA);
        var (configB, orgB) = LinkedInLoginEndpoints.ResolveConfig(ctxB);

        Assert.NotEqual(orgA, orgB);
        Assert.Equal(configA.AccountLabel, configB.AccountLabel);
        Assert.NotEqual(
            LinkedInCommand.CacheKey(orgA, configA.AccountLabel),
            LinkedInCommand.CacheKey(orgB, configB.AccountLabel));
    }

    [Fact]
    public void ResolveConfig_NoOrgHeaderFallsBackToDefaultAccount()
    {
        var ctx = new DefaultHttpContext();
        var (config, orgId) = LinkedInLoginEndpoints.ResolveConfig(ctx);

        Assert.Equal(Guid.Empty, orgId);
        Assert.Equal("default", config.AccountLabel);
    }

    [Fact]
    public void ResolveConfig_ProfileDirForLoginMatchesProductionDoWorkPath_ForTheSameOrgAndAccountLabel()
    {
        // WP6A-7 (third pass): the directory LinkedInLoginSession actually authenticates into
        // (LinkedInPaths.ProfileDir keyed by the resolved caller org) must be byte-for-byte the
        // one LinkedInCommand.GetBrowser resolves for that SAME org's next ordinary tool call, or
        // completing login opens a fresh, unauthenticated profile for that call. Both call sites
        // now key off the identical, real, resolved org id ResolveConfig hands back -- no more
        // sentinel indirection (the old LinkedInLoginSession.ProfileOrgId == Guid.Empty bucket).
        var ctx = new DefaultHttpContext();
        ctx.Request.Headers["X-Organization-Id"] = Guid.NewGuid().ToString();

        var (config, orgId) = LinkedInLoginEndpoints.ResolveConfig(ctx);

        var loginProfileDir = LinkedInPaths.ProfileDir(orgId, config.AccountLabel);
        var productionProfileDir = LinkedInPaths.ProfileDir(orgId, config.AccountLabel);

        Assert.Equal(productionProfileDir, loginProfileDir);
        Assert.NotEqual(LinkedInPaths.ProfileDir(Guid.Empty, config.AccountLabel), loginProfileDir);
    }

    [Fact]
    public void ResolveConfig_DifferentOrgsResolveToDifferentProfileDirectories_ForTheSameAccountLabel()
    {
        // WP6A-7 (third pass) core regression: two orgs that both leave AccountLabel at its
        // "default" default must resolve to physically distinct profile directories end to end,
        // starting from the exact org id ResolveConfig hands the login flow.
        var ctxA = new DefaultHttpContext();
        ctxA.Request.Headers["X-Organization-Id"] = Guid.NewGuid().ToString();
        var ctxB = new DefaultHttpContext();
        ctxB.Request.Headers["X-Organization-Id"] = Guid.NewGuid().ToString();

        var (configA, orgA) = LinkedInLoginEndpoints.ResolveConfig(ctxA);
        var (configB, orgB) = LinkedInLoginEndpoints.ResolveConfig(ctxB);

        Assert.NotEqual(
            LinkedInPaths.ProfileDir(orgA, configA.AccountLabel),
            LinkedInPaths.ProfileDir(orgB, configB.AccountLabel));
    }

    // ---- LinkedInCommand.CacheKey (backs both the production browser cache and the
    //      LinkedInLoginSession.Active dictionary) ----

    [Fact]
    public void CacheKey_DifferentOrgsSameAccountLabelProduceDifferentKeys()
    {
        var keyA = LinkedInCommand.CacheKey(Guid.NewGuid(), "default");
        var keyB = LinkedInCommand.CacheKey(Guid.NewGuid(), "default");
        Assert.NotEqual(keyA, keyB);
    }

    [Fact]
    public void CacheKey_SameOrgAndAccountProduceTheSameKey()
    {
        var orgId = Guid.NewGuid();
        Assert.Equal(LinkedInCommand.CacheKey(orgId, "Primary"), LinkedInCommand.CacheKey(orgId, "  primary  "));
    }

    [Fact]
    public void CacheKey_EmptyOrgIsItsOwnUnscopedBucket_DistinctFromAnyRealOrg()
    {
        var unscoped = LinkedInCommand.CacheKey(Guid.Empty, "default");
        var scoped = LinkedInCommand.CacheKey(Guid.NewGuid(), "default");
        Assert.NotEqual(unscoped, scoped);
    }

    // ---- LinkedInLoginSession isolation built on CacheKey ----

    [Fact]
    public void ActiveFor_DifferentOrgsNeverObserveEachOthersSession_WhenNoneIsActive()
    {
        // No session is running for either key (nothing was started in this test), so both must
        // report "not active" independently — i.e. one org's lookup can never accidentally
        // resolve to a dictionary entry keyed for a different org.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        Assert.Null(LinkedInLoginSession.ActiveFor("default", orgA));
        Assert.Null(LinkedInLoginSession.ActiveFor("default", orgB));
    }

    [Fact]
    public void ActiveFor_OrgBCannotObserveOrgAsActuallyInFlightSession()
    {
        // Minor finding from the adversarial review: the test above never actually starts a
        // session, so it doesn't prove cross-org isolation of an in-flight one. StartAsync itself
        // spawns real Xvfb/x11vnc/websockify processes (integration-only, see the class doc), so
        // this registers a real LinkedInLoginSession into the private `Active` dictionary the
        // same way StartAsync does, without spawning anything, to prove org B's lookup really
        // can't see org A's in-flight session.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var config = new ServiceConfig { AccountLabel = "default" };
        LogDelegate log = (_, _, _) => Task.CompletedTask;

        var sessionType = typeof(LinkedInLoginSession);
        var ctor = sessionType.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(ServiceConfig), typeof(LogDelegate), typeof(Guid) },
            null)!;
        var session = (LinkedInLoginSession)ctor.Invoke(new object[] { config, log, orgA });

        var activeField = sessionType.GetField("Active", BindingFlags.NonPublic | BindingFlags.Static)!;
        var active = (Dictionary<string, LinkedInLoginSession>)activeField.GetValue(null)!;
        var keyField = sessionType.GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var key = (string)keyField.GetValue(session)!;

        active[key] = session;
        try
        {
            Assert.Same(session, LinkedInLoginSession.ActiveFor("default", orgA));
            Assert.Null(LinkedInLoginSession.ActiveFor("default", orgB));
        }
        finally
        {
            active.Remove(key); // never leak static state into other tests
        }
    }

    // ---- WP6A-7 (third pass): LinkedInLoginSession no longer has a Guid.Empty profile sentinel;
    //      its own OrgId (the real, resolved caller org) is what it now authenticates into. ----

    [Fact]
    public void OrgId_IsTheRealResolvedCallerOrg_NotASentinel()
    {
        // Regression guard for the old design: a login session's browser/profile must resolve
        // under the SAME real org LinkedInCommand.DoWork's production path will use for that org
        // — never a fixed Guid.Empty sentinel shared by every tenant (the pre-third-pass design).
        var sessionType = typeof(LinkedInLoginSession);
        var ctor = sessionType.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(ServiceConfig), typeof(LogDelegate), typeof(Guid) },
            null)!;
        var orgId = Guid.NewGuid();
        var config = new ServiceConfig { AccountLabel = "default" };
        LogDelegate log = (_, _, _) => Task.CompletedTask;

        var session = (LinkedInLoginSession)ctor.Invoke(new object[] { config, log, orgId });

        Assert.Equal(orgId, session.OrgId);
        Assert.NotEqual(Guid.Empty, session.OrgId);
    }

    // ---- WP6A-7 re-review blocking #1: /login/start refuses a profile another org already owns ----

    [Fact]
    public void EnsureProfileNotOwnedByAnotherOrg_AllowsWhenNoOwnerRecordedYet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-guard-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Must not throw.
            LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg(Guid.NewGuid(), dir);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnsureProfileNotOwnedByAnotherOrg_AllowsTheSameOrgThatAlreadyOwnsIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-guard-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var orgId = Guid.NewGuid();
            LinkedInPaths.WriteProfileOwner(dir, orgId);

            // Must not throw: re-authenticating the same org's own profile is fine.
            LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg(orgId, dir);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnsureProfileNotOwnedByAnotherOrg_ThrowsWhenADifferentOrgAlreadyOwnsIt()
    {
        // This is the exact scenario from the re-review: Org A logs in and its login session
        // marks the shared (ProfileOrgId == Guid.Empty, AccountLabel == "default") profile dir as
        // owned by Org A. Org B, a different, unrelated org that also leaves AccountLabel at
        // "default", must never be allowed to open (and get handed a live noVNC session onto)
        // that same, already-authenticated profile.
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-guard-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();
            LinkedInPaths.WriteProfileOwner(dir, orgA);

            var ex = Assert.Throws<InvalidOperationException>(
                () => LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg(orgB, dir));
            Assert.Contains("account label", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void EnsureProfileNotOwnedByAnotherOrg_AllowsWhenCallerOrgIsUnresolved()
    {
        // Tenancy-disabled deployments (no X-Organization-Id header -> Guid.Empty) must keep
        // working exactly as before WP6A-7 -- this guard only ever fires between two distinct,
        // resolved orgs.
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-guard-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            LinkedInPaths.WriteProfileOwner(dir, Guid.NewGuid());

            LinkedInLoginSession.EnsureProfileNotOwnedByAnotherOrg(Guid.Empty, dir);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ---- WP6A-7 re-review blocking #2 (second pass): atomic ownership claim closes the
    //      pre-authentication TOCTOU. EnsureProfileNotOwnedByAnotherOrg above only ever consults
    //      a marker written AFTER Authenticated=true (LinkedInLoginSession.BeginPolling), so two
    //      orgs racing /login/start on the same (shared, default-label) profile dir before either
    //      one finishes interactive login were neither short-circuited (Active is keyed per org)
    //      nor blocked (no owner recorded yet) -- both got a live, concurrent Chromium session on
    //      the identical user-data-dir. LinkedInPaths.TryClaimProfile closes that window by
    //      claiming the marker atomically (FileMode.CreateNew) BEFORE any browser is launched.

    [Fact]
    public void TryClaimProfile_ClaimsFreshWhenNoMarkerExistsYet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-claim-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var orgA = Guid.NewGuid();

            var claim = LinkedInPaths.TryClaimProfile(orgA, dir);

            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, claim);
            Assert.Equal(orgA, LinkedInPaths.ReadProfileOwner(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryClaimProfile_SameOrgReclaimingItsOwnProfileIsAlreadyOwned()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-claim-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var orgA = Guid.NewGuid();
            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, LinkedInPaths.TryClaimProfile(orgA, dir));

            var second = LinkedInPaths.TryClaimProfile(orgA, dir);

            Assert.Equal(LinkedInPaths.ProfileClaim.AlreadyOwned, second);
            Assert.Equal(orgA, LinkedInPaths.ReadProfileOwner(dir)); // marker untouched, not clobbered
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryClaimProfile_RejectsADifferentResolvedOrgOnceAnotherOrgHasClaimedIt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-claim-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();
            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, LinkedInPaths.TryClaimProfile(orgA, dir));

            var claimB = LinkedInPaths.TryClaimProfile(orgB, dir);

            Assert.Equal(LinkedInPaths.ProfileClaim.Rejected, claimB);
            // Org B's rejected attempt must never overwrite org A's recorded ownership.
            Assert.Equal(orgA, LinkedInPaths.ReadProfileOwner(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TryClaimProfile_UnresolvedCallerIsAllowedEvenIfAnotherOrgAlreadyClaimedIt()
    {
        // Tenancy-disabled deployments (Guid.Empty) must keep working exactly as before WP6A-7.
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-claim-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            LinkedInPaths.WriteProfileOwner(dir, Guid.NewGuid());

            var claim = LinkedInPaths.TryClaimProfile(Guid.Empty, dir);

            Assert.Equal(LinkedInPaths.ProfileClaim.AlreadyOwned, claim);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task TryClaimProfile_ConcurrentStartOrdering_ExactlyOneOfTwoDifferentOrgsWinsTheRace()
    {
        // The scenario re-review flagged as still-open: org A and org B both call /login/start
        // against the same (shared, default-label) profile dir at effectively the same instant,
        // BEFORE either one has authenticated. With the marker only written post-authentication,
        // both would have raced past the check and driven a live browser on the identical
        // user-data-dir. TryClaimProfile must serialize the two so exactly one of them ever gets
        // to launch a browser on this directory.
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-claim-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var orgA = Guid.NewGuid();
            var orgB = Guid.NewGuid();
            using var barrier = new Barrier(2);

            LinkedInPaths.ProfileClaim ResultFor(Guid org)
            {
                barrier.SignalAndWait(TimeSpan.FromSeconds(5));
                return LinkedInPaths.TryClaimProfile(org, dir);
            }

            var taskA = Task.Run(() => ResultFor(orgA));
            var taskB = Task.Run(() => ResultFor(orgB));
            var completed = await Task.WhenAll(taskA, taskB);

            var results = completed;
            // Exactly one side must win the fresh claim; the other must be rejected outright
            // (never "AlreadyOwned" -- that would mean it matched a marker recording ITS OWN id,
            // which can't happen for two distinct orgs) so the loser never launches a browser.
            Assert.Single(results, r => r == LinkedInPaths.ProfileClaim.ClaimedFresh);
            Assert.Single(results, r => r == LinkedInPaths.ProfileClaim.Rejected);

            // The winner's org id is the only one ever recorded — no split-brain ownership.
            var owner = LinkedInPaths.ReadProfileOwner(dir);
            Assert.True(owner == orgA || owner == orgB);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ReleaseProfileClaim_RemovesTheMarkerSoAFailedLoginDoesNotPermanentlyLockTheLabel()
    {
        // WP6A-7 second pass: a claim made at StartAsync-time must be released on cancel/failure
        // (before authentication succeeds), or a single crashed/cancelled login would permanently
        // block that account label for its own org, let alone anyone else.
        var dir = Path.Combine(Path.GetTempPath(), "hq-linkedin-claim-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var orgA = Guid.NewGuid();
            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, LinkedInPaths.TryClaimProfile(orgA, dir));

            LinkedInPaths.ReleaseProfileClaim(dir);

            Assert.Null(LinkedInPaths.ReadProfileOwner(dir));
            // A different org can now claim the label the failed attempt left behind.
            var orgB = Guid.NewGuid();
            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, LinkedInPaths.TryClaimProfile(orgB, dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task StartAsync_SameOrgDoubleStartBeforeAuthenticatingIsAlreadyOwnedNotRejected()
    {
        // WP6A-7 (third pass): once ProfileDir is keyed by the real caller org, two DIFFERENT
        // orgs can no longer even contend for the same directory (see the next test) -- the
        // remaining race StartAsync's atomic claim guards is the SAME org racing itself (e.g. two
        // concurrent /login/start clicks) before either finishes authenticating. This drives the
        // exact same static entry point StartAsync uses -- LinkedInPaths.TryClaimProfile against
        // this org's own directory -- to prove that ordering.
        var orgA = Guid.NewGuid();
        var dir = LinkedInPaths.ProfileDir(orgA, "concurrent-start-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            // First "start" claims fresh (hasn't authenticated yet).
            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, LinkedInPaths.TryClaimProfile(orgA, dir));

            // A second concurrent start for the SAME org against its own directory is allowed
            // (AlreadyOwned) -- it's the org's own in-flight claim, not a foreign takeover.
            var second = LinkedInPaths.TryClaimProfile(orgA, dir);

            Assert.Equal(LinkedInPaths.ProfileClaim.AlreadyOwned, second);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        await Task.CompletedTask;
    }

    [Fact]
    public void StartAsync_TwoDifferentOrgsNeverContendForTheSameProfileDirectoryAnyMore()
    {
        // WP6A-7 (third pass) core regression: under the pre-third-pass design, StartAsync always
        // claimed the SAME shared (ProfileOrgId == Guid.Empty) directory for every org, so a
        // second, different org racing in had to be REJECTED by TryClaimProfile. Now that
        // StartAsync claims LinkedInPaths.ProfileDir(orgId, accountLabel) -- keyed by each org's
        // own real id -- two different orgs resolve to different directories and BOTH claim fresh
        // independently; there is no longer a directory for them to race on at all.
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var accountLabel = "concurrent-start-test-" + Guid.NewGuid().ToString("N");
        var dirA = LinkedInPaths.ProfileDir(orgA, accountLabel);
        var dirB = LinkedInPaths.ProfileDir(orgB, accountLabel);
        try
        {
            Assert.NotEqual(dirA, dirB);
            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, LinkedInPaths.TryClaimProfile(orgA, dirA));
            Assert.Equal(LinkedInPaths.ProfileClaim.ClaimedFresh, LinkedInPaths.TryClaimProfile(orgB, dirB));
        }
        finally
        {
            if (Directory.Exists(dirA)) Directory.Delete(dirA, recursive: true);
            if (Directory.Exists(dirB)) Directory.Delete(dirB, recursive: true);
        }
    }

    // ---- WP6A-11: x11vnc bound to loopback ----

    [Fact]
    public void X11VncArgs_BindsToLocalhostOnly()
    {
        var args = LinkedInLoginSession.X11VncArgs(":142", 142);
        Assert.Contains("-localhost", args);
    }

    [Fact]
    public void X11VncArgs_StaysUnauthenticatedButLoopbackScoped()
    {
        // -nopw is required (there's no VNC password flow) but must only ever be reachable via
        // -localhost -> the websockify/noVNC proxy, never directly off-host.
        var args = LinkedInLoginSession.X11VncArgs(":100", 100);
        Assert.Contains("-nopw", args);
        Assert.Contains("-localhost", args);
        Assert.Contains("-rfbport 6000", args);
    }
}
