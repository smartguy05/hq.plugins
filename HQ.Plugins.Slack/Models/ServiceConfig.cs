using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Slack.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Slack app-level token starting with xapp-. Found under Basic Information > App-Level Tokens.")]
    public string AppLevelToken { get; set; }

    [Tooltip("Slack bot token starting with xoxb-. Found under OAuth & Permissions > Bot User OAuth Token.")]
    public string BotToken { get; set; }

    [Tooltip("Name of the AI plugin to route incoming Slack messages to")]
    public string AiPlugin { get; set; }

    [Tooltip("Slack channel ID for sending notifications, e.g. C01ABCDEF23")]
    public string NotificationChannelId { get; set; }
}
