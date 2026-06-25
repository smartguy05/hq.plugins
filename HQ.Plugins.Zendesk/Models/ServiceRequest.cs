using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Zendesk.Models;

/// <summary>Handles JSON values that may arrive as either strings or numbers.</summary>
public class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => reader.GetString()
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>
/// Framework request envelope (the <c>T</c> in <c>CommandBase&lt;T, ServiceConfig&gt;</c>). Carries
/// only the orchestrator-supplied routing fields; per-tool LLM arguments now live on each tool's
/// dedicated args type (see <c>ToolArgs.cs</c>) and are bound by <c>ProcessRequest</c>.
/// </summary>
public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
}
