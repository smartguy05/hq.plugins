using HQ.Plugins.Telegram.Models;

namespace HQ.Plugins.Telegram;

/// <summary>
/// WP6B-3: TelegramService.ListenForMessages used to route ANY inbound Telegram DM straight to
/// the orchestrator — no allowlist of any kind, and a first-message-wins static chat id that let
/// a stranger hijack the bot's default outbound recipient. This is a pure, deny-by-default gate:
/// an update is allowed only if its chat id is explicitly configured (via
/// <see cref="ServiceConfig.AllowedChatIds"/>, falling back to
/// <see cref="ServiceConfig.NotificationChatId"/> alone when that list is empty) and, if
/// <see cref="ServiceConfig.AllowedUserIds"/> is set, its sender id is in that list too. Nothing
/// configured at all means nothing is allowed — the previous "first sender becomes trusted"
/// behaviour is gone.
/// </summary>
public static class TelegramAccessControl
{
    public static bool IsUpdateAllowed(ServiceConfig config, long? chatId, long? userId)
    {
        if (chatId is null) return false;

        var allowedChats = ParseIds(config?.AllowedChatIds);
        if (allowedChats.Count == 0 && long.TryParse(config?.NotificationChatId, out var defaultChatId))
        {
            allowedChats.Add(defaultChatId);
        }

        if (allowedChats.Count == 0 || !allowedChats.Contains(chatId.Value))
            return false;

        var allowedUsers = ParseIds(config?.AllowedUserIds);
        if (allowedUsers.Count > 0 && (userId is null || !allowedUsers.Contains(userId.Value)))
            return false;

        return true;
    }

    private static HashSet<long> ParseIds(string csv)
    {
        var set = new HashSet<long>();
        if (string.IsNullOrWhiteSpace(csv)) return set;

        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(part, out var id))
                set.Add(id);
        }

        return set;
    }
}
