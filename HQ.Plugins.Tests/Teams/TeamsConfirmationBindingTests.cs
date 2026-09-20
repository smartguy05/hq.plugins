using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Interfaces;
using HQ.Plugins.Teams;
using HQ.Plugins.Teams.Models;
using Moq;

namespace HQ.Plugins.Tests.Teams;

/// <summary>
/// WP6B-4: TeamsBot used a process-global `public static Confirmation PendingConfirmation` slot
/// with no binding to which channel/conversation it was posted to. Any inbound text message (or
/// Adaptive Card Action.Submit) anywhere the bot could see — a different channel, a different
/// team, even an unrelated 1:1 chat with the same bot — that happened to match the pending
/// option's key would approve it, and because the slot was `static`, one agent's Teams plugin
/// instance could clobber (or approve) a completely different agent's pending confirmation in
/// the same process. PendingConfirmationStore replaces it: instance-scoped (one per TeamsBot,
/// i.e. one per agent's Teams config) and keyed by the Teams channel id the confirmation card
/// was actually posted to, so only a reply arriving on that same channel can ever match.
/// </summary>
public class TeamsConfirmationBindingTests
{
    private static Confirmation MakeConfirmation(string id = null) => new()
    {
        Id = Guid.Parse(id ?? "11111111-1111-1111-1111-111111111111"),
        ConfirmationMessage = "Proceed?",
        Options = new Dictionary<string, bool> { ["yes"] = true, ["no"] = false }
    };

    [Fact]
    public void Store_TryGet_ReturnsConfirmation_ForMatchingChannelKey()
    {
        var store = new PendingConfirmationStore();
        var confirmation = MakeConfirmation();

        store.Set("channel-A", confirmation);

        Assert.True(store.TryGet("channel-A", out var found));
        Assert.Equal(confirmation.Id, found.Id);
    }

    [Fact]
    public void Store_TryGet_DoesNotLeakAcrossDifferentChannelKeys()
    {
        var store = new PendingConfirmationStore();
        var confirmationA = MakeConfirmation("11111111-1111-1111-1111-111111111111");

        store.Set("channel-A", confirmationA);

        // A reply arriving on an unrelated channel (or a 1:1 chat, key null/"") must never see
        // channel A's pending confirmation.
        Assert.False(store.TryGet("channel-B", out _));
        Assert.False(store.TryGet(null, out _));
    }

    [Fact]
    public void Store_SecondChannelsConfirmation_DoesNotClobberTheFirst()
    {
        var store = new PendingConfirmationStore();
        var confirmationA = MakeConfirmation("11111111-1111-1111-1111-111111111111");
        var confirmationB = MakeConfirmation("22222222-2222-2222-2222-222222222222");

        store.Set("channel-A", confirmationA);
        store.Set("channel-B", confirmationB);

        Assert.True(store.TryGet("channel-A", out var foundA));
        Assert.Equal(confirmationA.Id, foundA.Id);
        Assert.True(store.TryGet("channel-B", out var foundB));
        Assert.Equal(confirmationB.Id, foundB.Id);
    }

    [Fact]
    public void Store_Remove_ClearsOnlyThatChannelKey()
    {
        var store = new PendingConfirmationStore();
        store.Set("channel-A", MakeConfirmation());
        store.Set("channel-B", MakeConfirmation());

        store.Remove("channel-A");

        Assert.False(store.TryGet("channel-A", out _));
        Assert.True(store.TryGet("channel-B", out _));
    }

    [Fact]
    public void TeamsBot_NoLongerHasProcessGlobalStaticConfirmationSlot()
    {
        var staticFields = typeof(TeamsBot).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.DoesNotContain(staticFields, f => f.Name == "PendingConfirmation");
    }

    private static Task NoopLog(HQ.Models.Enums.LogLevel level, string message, Exception ex = null) => Task.CompletedTask;

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// WP6B-4 review follow-up: NotificationChannelId is optional (a Teams bot used only in 1:1
    /// chat never sets it), and TeamsGetChannelId() returns null for every 1:1 chat -- both
    /// coalesce to the same "" key in PendingConfirmationStore. RequestConfirmation used to call
    /// SetPendingConfirmation unconditionally, before/regardless of whether SendConfirmationCard
    /// actually posted the card anywhere, so an unconfigured NotificationChannelId meant every
    /// pending confirmation collapsed onto the exact "" bucket that any stranger's 1:1 DM to the
    /// bot also resolves to -- reproducing the original cross-tenant approval-forgery bug for
    /// this plausible, unconfigured-channel state.
    /// </summary>
    [Fact]
    public async Task RequestConfirmation_WithNoNotificationChannelConfigured_DoesNotBindToTheBlankChannelKey()
    {
        var command = new TeamsCommand();
        var config = new ServiceConfig
        {
            Name = "Teams",
            ClientId = "client-id",
            ClientSecret = "client-secret",
            TenantId = "tenant-id",
            BotAppId = "app-id",
            BotAppPassword = "app-password",
            ListenerPort = GetFreeTcpPort(),
            ListenerPath = "/api/messages",
            AiPlugin = "OpenAi"
            // NotificationTeamId / NotificationChannelId deliberately left unset: a supported,
            // unremarkable config for a bot only ever used in 1:1 chat.
        };

        await command.Initialize(JsonSerializer.Serialize(config), NoopLog, Mock.Of<INotificationService>());

        try
        {
            var confirmation = MakeConfirmation();

            var result = await command.RequestConfirmation(confirmation, new OrchestratorRequest());

            // SendConfirmationCard must have refused to send (no channel configured) ...
            Assert.False((bool)result.GetType().GetProperty("Success")!.GetValue(result)!);

            // ... and, critically, RequestConfirmation must not have stored the confirmation
            // under the blank channel key -- that is the exact key every 1:1 DM with the bot
            // also resolves to (TeamsGetChannelId() returns null there), so storing it would let
            // any stranger who has ever DMed the bot approve a confirmation nobody actually saw.
            Assert.False(command.HasPendingConfirmationForChannel(""));
            Assert.False(command.HasPendingConfirmationForChannel(null));
        }
        finally
        {
            await command.Dispose();
        }
    }
}
