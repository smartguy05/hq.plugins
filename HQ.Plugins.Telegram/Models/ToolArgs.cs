using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.Telegram.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

public class SendTelegramMessageArgs
{
    [Required, Description("The message text to send")]
    public string MessageText { get; set; }

    [Description("The Telegram chat ID to send the message to. Optional, defaults to configured notification chat.")]
    public string ChatId { get; set; }
}
