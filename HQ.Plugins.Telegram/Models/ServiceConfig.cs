using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Telegram.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Bot token from @BotFather, e.g. 123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11")]
    public string BotToken { get; set; }

    [Tooltip("Name of the AI plugin to route incoming messages to")]
    public string AiPlugin { get; set; }

    [Tooltip("Telegram chat ID for sending notifications. Use @userinfobot to find yours.")]
    public string NotificationChatId { get; set; }

    [Tooltip("How often to poll Telegram for new messages, in milliseconds")]
    public int PollingIntervalInMs { get; set; } = 1000;
}
