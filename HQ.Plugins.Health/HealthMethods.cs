namespace HQ.Plugins.Health;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on HealthService.</summary>
public static class HealthMethods
{
    public const string ListUsers = "list_health_users";
    public const string GetSleep = "get_sleep";
    public const string GetActivity = "get_activity";
    public const string GetDaily = "get_daily";
    public const string GetBody = "get_body";
}
