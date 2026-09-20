using System.Reflection;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.LinkedIn;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.Tests.LinkedIn;

/// <summary>
/// Shares the "LinkedIn command static state" collection with <see cref="LinkedInTenancyTests"/>:
/// <see cref="LinkedInCommand.DoWork"/> below writes the process-wide
/// <see cref="LinkedInCommand.LastConfig"/> static as a documented side effect, which
/// <c>LinkedInTenancyTests.ResolveConfig_*</c> reads. Without this shared, non-parallel collection
/// the two classes could run concurrently on separate threads and race on that static (see
/// <see cref="LinkedInStaticStateCollection"/> for the full history). The constructor/Dispose
/// reset additionally makes each test in this class hermetic regardless of ordering.
/// </summary>
[Collection("LinkedIn command static state")]
public class LinkedInCommandTests : IDisposable
{
    private readonly LinkedInCommand _command = new();

    public LinkedInCommandTests() => LinkedInCommand.ResetForTests();

    public void Dispose() => LinkedInCommand.ResetForTests();

    [Fact]
    public void Name_ReturnsLinkedIn() => Assert.Equal("LinkedIn", _command.Name);

    [Fact]
    public void Description_IsNotEmpty() => Assert.False(string.IsNullOrWhiteSpace(_command.Description));

    [Fact]
    public void GetToolDefinitions_ReturnsAllTools()
    {
        // 9 original tools preserved + react_to_post + 4 search/enrichment tools.
        var tools = _command.GetToolDefinitions();
        Assert.Equal(14, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveNameDescriptionAndParameters()
    {
        var tools = _command.GetToolDefinitions();
        Assert.All(tools, tool =>
        {
            Assert.NotNull(tool.Function);
            Assert.False(string.IsNullOrWhiteSpace(tool.Function.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Function.Description));
            Assert.NotNull(tool.Function.Parameters);
        });
    }

    // Every tool the original (Relevance AI) plugin exposed must still be present.
    [Theory]
    [InlineData("get_all_chats")]
    [InlineData("get_chat_messages")]
    [InlineData("get_user_profile")]
    [InlineData("create_post")]
    [InlineData("send_comment")]
    [InlineData("get_inmail_balance")]
    [InlineData("send_invitation")]
    [InlineData("send_message")]
    [InlineData("start_new_chat")]
    public void GetToolDefinitions_PreservesOriginalTools(string toolName)
    {
        var tools = _command.GetToolDefinitions();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }

    // New search/enrichment + engagement tools added on top.
    [Theory]
    [InlineData("react_to_post")]
    [InlineData("search_people")]
    [InlineData("lookup_person")]
    [InlineData("search_companies")]
    [InlineData("lookup_company")]
    public void GetToolDefinitions_AddsNewTools(string toolName)
    {
        var tools = _command.GetToolDefinitions();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }

    // ======================================================================================
    // WP6A-7 (third pass): the production per-agent tool-call path (DoWork -> GetBrowser) must
    // resolve a real, host-injected caller org id and fail closed without one. Covers the three
    // scenarios called out in the finding's fix: (1) null org is refused, (2) two orgs resolve
    // distinct cached browsers/profile directories, (3) a profile already owned by a different
    // org is refused.
    // ======================================================================================

    private static readonly MethodInfo DoWorkMethod = typeof(LinkedInCommand)
        .GetMethod("DoWork", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static async Task<(bool Success, string Message)> InvokeDoWork(LinkedInCommand command, ServiceRequest request, ServiceConfig config)
    {
        var task = (Task<object>)DoWorkMethod.Invoke(command, new object[] { request, config, Array.Empty<ToolCall>() })!;
        var result = await task;
        var type = result.GetType();
        var success = (bool)type.GetProperty("Success")!.GetValue(result)!;
        var message = (string)type.GetProperty("Message")!.GetValue(result)!;
        return (success, message);
    }

    // ---- RequireOrganizationId (pure fail-closed guard) ----

    [Fact]
    public void RequireOrganizationId_NullIsRefused()
        => Assert.Throws<UnauthorizedAccessException>(() => LinkedInCommand.RequireOrganizationId(null));

    [Fact]
    public void RequireOrganizationId_EmptyGuidIsRefused()
        => Assert.Throws<UnauthorizedAccessException>(() => LinkedInCommand.RequireOrganizationId(Guid.Empty));

    [Fact]
    public void RequireOrganizationId_ResolvedOrgIsReturnedUnchanged()
    {
        var orgId = Guid.NewGuid();
        Assert.Equal(orgId, LinkedInCommand.RequireOrganizationId(orgId));
    }

    // ---- DoWork fails closed on a null/missing injected org, before ever touching a browser ----

    [Fact]
    public async Task DoWork_NullOrganizationId_IsRefused_WithoutLaunchingABrowser()
    {
        var command = new LinkedInCommand { Logger = (_, _, _) => Task.CompletedTask };
        var config = new ServiceConfig { AccountLabel = "wp6a7-null-org-" + Guid.NewGuid().ToString("N") };
        var request = new ServiceRequest { Method = "get_all_chats", OrganizationId = null };

        var (success, message) = await InvokeDoWork(command, request, config);

        Assert.False(success);
        Assert.Contains("organization", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DoWork_EmptyGuidOrganizationId_IsRefused()
    {
        var command = new LinkedInCommand { Logger = (_, _, _) => Task.CompletedTask };
        var config = new ServiceConfig { AccountLabel = "wp6a7-empty-org-" + Guid.NewGuid().ToString("N") };
        var request = new ServiceRequest { Method = "get_all_chats", OrganizationId = Guid.Empty };

        var (success, message) = await InvokeDoWork(command, request, config);

        Assert.False(success);
        Assert.Contains("organization", message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- GetBrowser: per-(org, accountLabel) cache, no static/unscoped browser reachable ----

    [Fact]
    public void GetBrowser_DifferentOrgsSameAccountLabelResolveDistinctCachedInstances()
    {
        var accountLabel = "wp6a7-cache-" + Guid.NewGuid().ToString("N");
        var config = new ServiceConfig { AccountLabel = accountLabel };
        LogDelegate log = (_, _, _) => Task.CompletedTask;
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        try
        {
            var browserA = LinkedInCommand.GetBrowser(config, log, orgA);
            var browserB = LinkedInCommand.GetBrowser(config, log, orgB);
            var browserAAgain = LinkedInCommand.GetBrowser(config, log, orgA);

            Assert.NotSame(browserA, browserB); // two orgs never share a live browser
            Assert.Same(browserA, browserAAgain); // same org's repeat call reuses its own cached one
        }
        finally
        {
            LinkedInCommand.InvalidateBrowser(accountLabel, orgA);
            LinkedInCommand.InvalidateBrowser(accountLabel, orgB);
        }
    }

    [Fact]
    public void GetBrowser_TwoOrgsSameAccountLabelResolveDifferentProfileDirectories()
    {
        // Companion to the cache-instance test above: proves the underlying on-disk profile,
        // not just the in-memory cache slot, differs per org for the ordinary tool-call path.
        var accountLabel = "wp6a7-dir-" + Guid.NewGuid().ToString("N");
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();

        var dirA = LinkedInPaths.ProfileDir(orgA, accountLabel);
        var dirB = LinkedInPaths.ProfileDir(orgB, accountLabel);

        Assert.NotEqual(dirA, dirB);
    }

    [Fact]
    public void GetBrowser_RefusesWhenTheResolvedProfileIsOwnedByADifferentOrg()
    {
        var accountLabel = "wp6a7-owner-" + Guid.NewGuid().ToString("N");
        var config = new ServiceConfig { AccountLabel = accountLabel };
        LogDelegate log = (_, _, _) => Task.CompletedTask;
        var callerOrg = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var profileDir = LinkedInPaths.ProfileDir(callerOrg, accountLabel);
        // Simulate a stale/foreign ownership marker already sitting in this org's resolved
        // directory (e.g. a manually copied/restored profile) -- GetBrowser must refuse rather
        // than silently launching Chromium onto another org's authenticated session.
        LinkedInPaths.WriteProfileOwner(profileDir, otherOrg);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => LinkedInCommand.GetBrowser(config, log, callerOrg));
            Assert.Contains("organization", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            LinkedInCommand.InvalidateBrowser(accountLabel, callerOrg);
            CleanUpProfileDir(profileDir);
        }
    }

    [Fact]
    public void GetBrowser_AllowsWhenTheResolvedProfileIsAlreadyOwnedByTheSameCallerOrg()
    {
        var accountLabel = "wp6a7-same-owner-" + Guid.NewGuid().ToString("N");
        var config = new ServiceConfig { AccountLabel = accountLabel };
        LogDelegate log = (_, _, _) => Task.CompletedTask;
        var callerOrg = Guid.NewGuid();
        var profileDir = LinkedInPaths.ProfileDir(callerOrg, accountLabel);
        LinkedInPaths.WriteProfileOwner(profileDir, callerOrg); // this org's own prior login

        try
        {
            var browser = LinkedInCommand.GetBrowser(config, log, callerOrg);
            Assert.NotNull(browser);
        }
        finally
        {
            LinkedInCommand.InvalidateBrowser(accountLabel, callerOrg);
            CleanUpProfileDir(profileDir);
        }
    }

    private static void CleanUpProfileDir(string profileDir)
    {
        if (Directory.Exists(profileDir)) Directory.Delete(profileDir, recursive: true);
        var accountDir = Path.GetDirectoryName(profileDir);
        if (accountDir != null && Directory.Exists(accountDir) && !Directory.EnumerateFileSystemEntries(accountDir).Any())
        {
            Directory.Delete(accountDir);
            var orgDir = Path.GetDirectoryName(accountDir);
            if (orgDir != null && Directory.Exists(orgDir) && !Directory.EnumerateFileSystemEntries(orgDir).Any())
                Directory.Delete(orgDir);
        }
    }
}
