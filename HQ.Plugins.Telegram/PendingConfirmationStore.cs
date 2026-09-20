using System.Collections.Concurrent;
using HQ.Models;

namespace HQ.Plugins.Telegram;

/// <summary>
/// WP6B-4: replaces the old process-global `static Confirmation PendingConfirmation` slot.
/// Keyed by the chat id the confirmation was posted to, so:
///  - one agent/org's confirmation can never be approved by inbound traffic on a different
///    chat (the previous single static slot had no such binding at all — any inbound message
///    anywhere that matched the option text would approve whatever was currently pending);
///  - being an instance field (one per TelegramService, one per agent's Telegram config) rather
///    than static, it can never be shared across agents/orgs in the same process either.
/// </summary>
public sealed class PendingConfirmationStore
{
    private readonly ConcurrentDictionary<string, Confirmation> _pending = new(StringComparer.Ordinal);

    public void Set(string chatKey, Confirmation confirmation) =>
        _pending[chatKey ?? string.Empty] = confirmation;

    public bool TryGet(string chatKey, out Confirmation confirmation) =>
        _pending.TryGetValue(chatKey ?? string.Empty, out confirmation);

    public void Remove(string chatKey) => _pending.TryRemove(chatKey ?? string.Empty, out _);
}
