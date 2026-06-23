namespace HQ.Plugins.LinkedIn;

/// <summary>
/// Action categories that carry per-day caps. Reads that aren't searches
/// (e.g. listing your own chats) are unmetered.
/// </summary>
public enum RateLimitCategory
{
    Invitation,
    Message,
    Search
}

/// <summary>
/// In-process, per-UTC-day counter enforcing conservative daily caps so the agent
/// can't burst LinkedIn actions in a way that gets a real account flagged. Pure and
/// deterministic: the "current time" is injected, so the rollover logic is unit-testable
/// without wall-clock dependence. Counts reset when the UTC date changes.
/// </summary>
public sealed class RateLimitGate
{
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<RateLimitCategory, int> _counts = new();
    private DateOnly _day;
    private readonly object _lock = new();

    public RateLimitGate(Func<DateTime> utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _day = DateOnly.FromDateTime(_utcNow());
    }

    /// <summary>Current count for a category on the active day (after rollover).</summary>
    public int Count(RateLimitCategory category)
    {
        lock (_lock)
        {
            Rollover();
            return _counts.TryGetValue(category, out var c) ? c : 0;
        }
    }

    /// <summary>
    /// Reserves one unit against <paramref name="dailyCap"/> for the category.
    /// Returns true and increments when within cap; returns false (no increment)
    /// when the cap is already reached.
    /// </summary>
    public bool TryConsume(RateLimitCategory category, int dailyCap)
    {
        lock (_lock)
        {
            Rollover();
            var current = _counts.TryGetValue(category, out var c) ? c : 0;
            if (dailyCap <= 0 || current >= dailyCap) return false;
            _counts[category] = current + 1;
            return true;
        }
    }

    private void Rollover()
    {
        var today = DateOnly.FromDateTime(_utcNow());
        if (today == _day) return;
        _day = today;
        _counts.Clear();
    }
}
