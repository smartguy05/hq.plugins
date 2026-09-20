using System.Collections.Concurrent;
using HQ.Models;

namespace HQ.Plugins.Teams;

/// <summary>
/// WP6B-4: replaces the old process-global `public static Confirmation PendingConfirmation`
/// slot on <see cref="TeamsBot"/>. Keyed by the Teams channel id the confirmation was posted
/// to, so:
///  - a reply arriving in a different channel, a different team, or an unrelated 1:1 chat with
///    the same bot can never approve it (the previous single static slot had no such binding —
///    any inbound message/card action anywhere that matched the option text or key would
///    approve whatever was currently pending);
///  - being an instance field (one per TeamsBot, one per agent's Teams config) rather than
///    static, it can never be shared across agents/orgs in the same process either.
/// </summary>
public sealed class PendingConfirmationStore
{
    private readonly ConcurrentDictionary<string, Confirmation> _pending = new(StringComparer.Ordinal);

    public void Set(string channelKey, Confirmation confirmation) =>
        _pending[channelKey ?? string.Empty] = confirmation;

    public bool TryGet(string channelKey, out Confirmation confirmation) =>
        _pending.TryGetValue(channelKey ?? string.Empty, out confirmation);

    public void Remove(string channelKey) => _pending.TryRemove(channelKey ?? string.Empty, out _);
}
