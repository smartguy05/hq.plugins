using System.Text.Json;
using System.Text.Json.Serialization;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Asana.Models;

/// <summary>
/// Handles JSON values that may arrive as either strings or numbers (LLMs often send
/// numeric IDs without quotes). Converts both to a C# string.
/// </summary>
public class StringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Number => reader.GetInt64().ToString(),
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Null => null,
            _ => reader.GetString()
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Task fields — converter handles LLMs sending numeric GIDs without quotes
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string TaskId { get; set; }
    public string Name { get; set; }
    public string Notes { get; set; }
    public string HtmlNotes { get; set; }
    public string Assignee { get; set; }
    public string DueOn { get; set; }
    public string DueAt { get; set; }
    public string StartOn { get; set; }
    public bool? Completed { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string Parent { get; set; }
    public string Followers { get; set; }
    public string CustomFields { get; set; }

    // Project fields
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string ProjectId { get; set; }
    [JsonConverter(typeof(StringOrNumberConverter))]
    public string SectionId { get; set; }
    public bool? Archived { get; set; }

    // Aliases — LLMs sometimes use "project"/"section" instead of "projectId"/"sectionId",
    // and may send numeric GIDs instead of strings.
    [JsonPropertyName("project")]
    public JsonElement? ProjectAlias { set => ProjectId ??= value?.ToString(); }

    [JsonPropertyName("section")]
    public JsonElement? SectionAlias { set => SectionId ??= value?.ToString(); }

    // Workspace/org fields
    public string Workspace { get; set; }
    public string Team { get; set; }

    // User fields
    public string UserId { get; set; }

    // Search fields
    public string Text { get; set; }
    public string Query { get; set; }
    public string ResourceType { get; set; }
    public string AssigneeAny { get; set; }
    public string ProjectsAny { get; set; }
    public string DueOnBefore { get; set; }
    public string DueOnAfter { get; set; }
    public string SortBy { get; set; }
    public bool? SortAscending { get; set; }

    // Story/comment fields
    public string StoryText { get; set; }
    public string HtmlText { get; set; }

    // Pagination & options
    public int? Limit { get; set; }
    public string Offset { get; set; }
    public string OptFields { get; set; }
    public int? Count { get; set; }

    // Flags
    public bool? IncludeSubtasks { get; set; }
    public bool? IncludeComments { get; set; }
}
