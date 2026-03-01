using HQ.Models.Interfaces;

namespace HQ.Plugins.Slack.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string AppLevelToken { get; set; }
    public string BotToken { get; set; }
    public string AiPlugin { get; set; }
    public string NotificationChannelId { get; set; }
}
