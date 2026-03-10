using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Teams.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Azure AD tenant ID from your App Registration, e.g. 12345678-abcd-1234-abcd-123456789abc")]
    public string TenantId { get; set; }

    [Tooltip("Application (client) ID from the Azure AD App Registration")]
    public string ClientId { get; set; }

    [Tooltip("Client secret value from the Azure AD App Registration")]
    public string ClientSecret { get; set; }

    [Tooltip("Bot Framework App ID. Usually the same as ClientId.")]
    public string BotAppId { get; set; }

    [Tooltip("Bot Framework App Password. Usually the same as ClientSecret.")]
    public string BotAppPassword { get; set; }

    [Tooltip("Port the bot listens on for incoming Teams messages")]
    public int ListenerPort { get; set; } = 3978;

    [Tooltip("URL path for the bot messaging endpoint")]
    public string ListenerPath { get; set; } = "/api/messages";

    [Tooltip("Name of the AI plugin to route incoming Teams messages to")]
    public string AiPlugin { get; set; }

    [Tooltip("Teams channel ID for sending notifications")]
    public string NotificationChannelId { get; set; }

    [Tooltip("Teams team ID containing the notification channel")]
    public string NotificationTeamId { get; set; }
}
