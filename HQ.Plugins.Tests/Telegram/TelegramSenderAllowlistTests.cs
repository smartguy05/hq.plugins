using System.Reflection;
using HQ.Plugins.Telegram;
using HQ.Plugins.Telegram.Models;

namespace HQ.Plugins.Tests.Telegram;

/// <summary>
/// WP6B-3: TelegramService.ListenForMessages used to route ANY inbound Telegram DM straight to
/// the orchestrator (no AgentId, no allowlist), and a process-wide static `_chatId` let whichever
/// stranger messaged the bot first become the default outbound recipient for the rest of the
/// process's life. TelegramAccessControl replaces both: an explicit allow-list gate (defaulting
/// to NotificationChatId, fail-closed when nothing is configured) and no mutable static state.
/// </summary>
public class TelegramSenderAllowlistTests
{
    private static ServiceConfig MakeConfig(string notificationChatId = null, string allowedChatIds = null, string allowedUserIds = null) => new()
    {
        Name = "Telegram",
        BotToken = "123456:fake-token",
        AiPlugin = "OpenAi",
        NotificationChatId = notificationChatId,
        AllowedChatIds = allowedChatIds,
        AllowedUserIds = allowedUserIds
    };

    [Fact]
    public void IsUpdateAllowed_NoAllowlistConfigured_DefaultsToNotificationChatIdOnly()
    {
        var config = MakeConfig(notificationChatId: "555");

        Assert.True(TelegramAccessControl.IsUpdateAllowed(config, chatId: 555, userId: 999));
        Assert.False(TelegramAccessControl.IsUpdateAllowed(config, chatId: 666, userId: 999));
    }

    [Fact]
    public void IsUpdateAllowed_NothingConfiguredAtAll_FailsClosed()
    {
        var config = MakeConfig();

        Assert.False(TelegramAccessControl.IsUpdateAllowed(config, chatId: 12345, userId: 1));
    }

    [Fact]
    public void IsUpdateAllowed_ExplicitAllowedChatIds_AllowsListedChatsOnly()
    {
        var config = MakeConfig(notificationChatId: "555", allowedChatIds: "111, 222,333");

        Assert.True(TelegramAccessControl.IsUpdateAllowed(config, chatId: 111, userId: 1));
        Assert.True(TelegramAccessControl.IsUpdateAllowed(config, chatId: 222, userId: 1));
        // Explicit allowlist replaces (does not add to) the NotificationChatId default.
        Assert.False(TelegramAccessControl.IsUpdateAllowed(config, chatId: 555, userId: 1));
        Assert.False(TelegramAccessControl.IsUpdateAllowed(config, chatId: 999, userId: 1));
    }

    [Fact]
    public void IsUpdateAllowed_AllowedUserIdsConfigured_AlsoRestrictsBySender()
    {
        var config = MakeConfig(notificationChatId: "555", allowedUserIds: "42");

        Assert.True(TelegramAccessControl.IsUpdateAllowed(config, chatId: 555, userId: 42));
        Assert.False(TelegramAccessControl.IsUpdateAllowed(config, chatId: 555, userId: 43));
        Assert.False(TelegramAccessControl.IsUpdateAllowed(config, chatId: 555, userId: null));
    }

    [Fact]
    public void IsUpdateAllowed_NullChatId_IsRejected()
    {
        var config = MakeConfig(notificationChatId: "555");

        Assert.False(TelegramAccessControl.IsUpdateAllowed(config, chatId: null, userId: 1));
    }

    [Fact]
    public void TelegramService_NoLongerHasProcessGlobalStaticState()
    {
        // WP6B-3 / WP6B-4: the process-wide `static Confirmation PendingConfirmation` and
        // `static long? _chatId` slots let one stranger's first DM, or one agent's confirmation,
        // leak across every agent/org sharing this plugin assembly. Pin that they're gone.
        var staticFields = typeof(TelegramService).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        Assert.DoesNotContain(staticFields, f => f.Name == "PendingConfirmation");
        Assert.DoesNotContain(staticFields, f => f.Name == "_chatId");
    }
}
