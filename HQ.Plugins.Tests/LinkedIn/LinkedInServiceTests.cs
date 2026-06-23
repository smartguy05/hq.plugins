using System.Text.Json;
using HQ.Plugins.LinkedIn;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInServiceTests
{
    private static ServiceConfig Config(bool requireConfirmation = false, int maxSearches = 80) => new()
    {
        Name = "LinkedIn",
        Description = "Test",
        AccountLabel = "test",
        RequiresConfirmation = requireConfirmation,
        MaxSearchesPerDay = maxSearches,
        MaxInvitationsPerDay = 20,
        MaxMessagesPerDay = 40
    };

    private static JsonElement Json(object result) =>
        JsonSerializer.SerializeToElement(result);

    // ---- argument validation ----

    [Fact]
    public async Task GetChatMessages_RequiresChatId()
    {
        var svc = new LinkedInService(new FakeLinkedInBrowser(), Config());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.GetChatMessages(Config(), new ServiceRequest { Method = "get_chat_messages" }));
    }

    [Fact]
    public async Task SearchPeople_RequiresQuery()
    {
        var svc = new LinkedInService(new FakeLinkedInBrowser(), Config());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.SearchPeople(Config(), new ServiceRequest { Method = "search_people" }));
    }

    // ---- reads route to the right Voyager endpoints ----

    [Fact]
    public async Task GetAllChats_ReturnsSuccessShape()
    {
        var browser = new FakeLinkedInBrowser { OnVoyager = (_, _, _) => new VoyagerResponse(200, "{\"elements\":[]}") };
        var svc = new LinkedInService(browser, Config());

        var result = Json(await svc.GetAllChats(Config(), new ServiceRequest()));

        Assert.True(result.GetProperty("Success").GetBoolean());
        Assert.Equal(200, result.GetProperty("Status").GetInt32());
        Assert.Equal(Voyager.Conversations, browser.Calls.Single().Path);
    }

    [Fact]
    public async Task SearchPeople_UsesPeopleTypeahead()
    {
        var browser = new FakeLinkedInBrowser { OnVoyager = (_, _, _) => new VoyagerResponse(200, "{\"elements\":[]}") };
        var svc = new LinkedInService(browser, Config());

        await svc.SearchPeople(Config(), new ServiceRequest { Query = "acme cto" });

        Assert.Contains("type=PEOPLE", browser.Calls.Single().Path);
        Assert.Contains("keywords=acme", browser.Calls.Single().Path);
    }

    [Fact]
    public async Task LookupCompany_UsesUniversalNameEndpoint()
    {
        var browser = new FakeLinkedInBrowser { OnVoyager = (_, _, _) => new VoyagerResponse(200, "{}") };
        var svc = new LinkedInService(browser, Config());

        await svc.LookupCompany(Config(), new ServiceRequest { CompanyId = "anthropic" });

        Assert.Contains("q=universalName", browser.Calls.Single().Path);
        Assert.Contains("universalName=anthropic", browser.Calls.Single().Path);
    }

    [Fact]
    public async Task FailedVoyagerCall_ReturnsUnsuccessfulShape()
    {
        var browser = new FakeLinkedInBrowser { OnVoyager = (_, _, _) => new VoyagerResponse(403, "blocked") };
        var svc = new LinkedInService(browser, Config());

        var result = Json(await svc.GetAllChats(Config(), new ServiceRequest()));

        Assert.False(result.GetProperty("Success").GetBoolean());
        Assert.Equal(403, result.GetProperty("Status").GetInt32());
    }

    // ---- confirmation gating on writes ----

    [Fact]
    public async Task CreatePost_WithConfirmationRequired_DoesNotExecuteOnFirstCall()
    {
        var browser = new FakeLinkedInBrowser();
        var notif = new FakeNotificationService();
        var config = Config(requireConfirmation: true);
        var svc = new LinkedInService(browser, config, notif);

        await svc.CreatePost(config, new ServiceRequest { Caption = "hello world" });

        Assert.Equal(1, notif.RequestCount);
        Assert.Empty(browser.Calls); // nothing posted until confirmed
    }

    [Fact]
    public async Task CreatePost_WithValidConfirmation_Executes()
    {
        var browser = new FakeLinkedInBrowser { OnVoyager = (_, _, _) => new VoyagerResponse(201, "{}") };
        var notif = new FakeNotificationService();
        var confirmationId = Guid.NewGuid();
        notif.Existing.Add(confirmationId);
        var config = Config(requireConfirmation: true);
        var svc = new LinkedInService(browser, config, notif);

        await svc.CreatePost(config, new ServiceRequest { Caption = "hi", ConfirmationId = confirmationId.ToString() });

        Assert.Single(browser.Calls);
        Assert.Equal(Voyager.Shares, browser.Calls.Single().Path);
    }

    [Fact]
    public async Task CreatePost_WithConfirmationDisabled_Executes()
    {
        var browser = new FakeLinkedInBrowser { OnVoyager = (_, _, _) => new VoyagerResponse(201, "{}") };
        var svc = new LinkedInService(browser, Config(), new FakeNotificationService());

        await svc.CreatePost(Config(), new ServiceRequest { Caption = "hi" });

        Assert.Single(browser.Calls);
    }

    // ---- rate limiting ----

    [Fact]
    public async Task SearchPeople_EnforcesDailyCap()
    {
        var browser = new FakeLinkedInBrowser { OnVoyager = (_, _, _) => new VoyagerResponse(200, "{\"elements\":[]}") };
        var gate = new RateLimitGate();
        var config = Config(maxSearches: 1);
        var svc = new LinkedInService(browser, config, notificationService: null, rateLimiter: gate);

        var first = Json(await svc.SearchPeople(config, new ServiceRequest { Query = "a" }));
        var second = Json(await svc.SearchPeople(config, new ServiceRequest { Query = "b" }));

        Assert.True(first.GetProperty("Success").GetBoolean());
        Assert.False(second.GetProperty("Success").GetBoolean());
        Assert.Single(browser.Calls); // the capped call never reached the browser
    }
}
