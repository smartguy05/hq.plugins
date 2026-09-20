using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Telegram.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>
    /// Injected by the host during initialization — identifies the agent that owns this plugin
    /// config (see PluginService.InjectAgentLlmConfig's "agentId" property, matched
    /// case-insensitively). Used to scope inbound Telegram messages to this agent (WP6B-3)
    /// instead of falling back to the host's cross-tenant default agent.
    /// </summary>
    public Guid? AgentId { get; set; }
    [Sensitive]
    [Tooltip("Bot token from @BotFather, e.g. 123456:ABC-DEF1234ghIkl-zyx57W2v1u123ew11")]
    public string BotToken { get; set; }

    [Tooltip("Name of the AI plugin to route incoming messages to")]
    public string AiPlugin { get; set; }

    [Tooltip("Telegram chat ID for sending notifications. Use @userinfobot to find yours.")]
    public string NotificationChatId { get; set; }

    [Tooltip("Comma-separated Telegram chat IDs allowed to message this bot. Leave blank to allow only NotificationChatId.")]
    public string AllowedChatIds { get; set; }

    [Tooltip("Optional comma-separated Telegram user IDs allowed to message this bot, in addition to the chat allowlist.")]
    public string AllowedUserIds { get; set; }

    [Tooltip("How often to poll Telegram for new messages, in milliseconds")]
    public int PollingIntervalInMs { get; set; } = 1000;
}
