using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.Notion.Models;

namespace HQ.Plugins.Notion;

/// <summary>
/// Tool surface for Notion. Simple cases are covered by title/text; advanced callers can pass raw
/// Notion JSON (properties/children/filter/sorts) since Notion's property schemas vary per database.
/// </summary>
public class NotionService
{
    private readonly LogDelegate _logger;

    public NotionService(LogDelegate logger) => _logger = logger;

    /// <summary>Build a Notion rich_text array from plain text.</summary>
    public static JsonArray RichText(string content) =>
        new(new JsonObject
        {
            ["type"] = "text",
            ["text"] = new JsonObject { ["content"] = content ?? "" }
        });

    /// <summary>Split plain text into Notion paragraph blocks (one per non-empty line).</summary>
    public static JsonArray ParagraphBlocks(string text)
    {
        var blocks = new JsonArray();
        if (string.IsNullOrWhiteSpace(text)) return blocks;
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            blocks.Add(new JsonObject
            {
                ["object"] = "block",
                ["type"] = "paragraph",
                ["paragraph"] = new JsonObject { ["rich_text"] = RichText(line) }
            });
        }
        return blocks;
    }

    private static JsonNode ParseOrNull(string json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);

    [Display(Name = NotionMethods.Search)]
    [Description("Search Notion pages and databases the integration can access.")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"Text to search titles for (empty returns all shared objects)"},"filterType":{"type":"string","description":"Limit to 'page' or 'database'"},"pageSize":{"type":"integer","description":"Max results (default 25)"}},"required":[]}""")]
    public Task<object> Search(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var body = new JsonObject { ["query"] = r.Query ?? "", ["page_size"] = r.PageSize ?? 25 };
            if (!string.IsNullOrWhiteSpace(r.FilterType))
                body["filter"] = new JsonObject { ["property"] = "object", ["value"] = r.FilterType };
            var doc = await client.PostAsync("/search", body);
            return new { Success = true, Results = Prop(doc, "results") };
        });

    [Display(Name = NotionMethods.GetPage)]
    [Description("Get a Notion page's properties by id.")]
    [Parameters("""{"type":"object","properties":{"pageId":{"type":"string"}},"required":["pageId"]}""")]
    public Task<object> GetPage(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/pages/{r.PageId}");
            return new { Success = true, Page = (object)doc };
        });

    [Display(Name = NotionMethods.CreatePage)]
    [Description("Create a page under a parent page or database. For a page parent, pass a title; for a database parent, pass propertiesJson matching the database schema. Optional text becomes paragraph blocks.")]
    [Parameters("""{"type":"object","properties":{"parentId":{"type":"string"},"parentType":{"type":"string","description":"'page' or 'database'"},"title":{"type":"string","description":"Title (page parent, or a 'title' property)"},"text":{"type":"string","description":"Optional body text"},"propertiesJson":{"type":"string","description":"Raw Notion properties object as JSON (overrides title)"},"childrenJson":{"type":"string","description":"Raw Notion block children array as JSON (overrides text)"}},"required":["parentId"]}""")]
    public Task<object> CreatePage(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var isDb = string.Equals(r.ParentType, "database", StringComparison.OrdinalIgnoreCase);
            var body = new JsonObject
            {
                ["parent"] = new JsonObject { [isDb ? "database_id" : "page_id"] = r.ParentId }
            };

            var props = ParseOrNull(r.PropertiesJson);
            if (props is not null) body["properties"] = props;
            else body["properties"] = new JsonObject { ["title"] = new JsonObject { ["title"] = RichText(r.Title) } };

            var children = ParseOrNull(r.ChildrenJson) ?? ParagraphBlocks(r.Text);
            if (children is JsonArray arr && arr.Count > 0) body["children"] = children;

            var doc = await client.PostAsync("/pages", body);
            return new { Success = true, Page = (object)doc };
        });

    [Display(Name = NotionMethods.AppendBlock)]
    [Description("Append content blocks to a page or block. Provide text (becomes paragraphs) or raw childrenJson.")]
    [Parameters("""{"type":"object","properties":{"blockId":{"type":"string","description":"Page id or block id to append to"},"text":{"type":"string"},"childrenJson":{"type":"string","description":"Raw Notion block children array as JSON (overrides text)"}},"required":["blockId"]}""")]
    public Task<object> AppendBlock(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var children = ParseOrNull(r.ChildrenJson) ?? ParagraphBlocks(r.Text);
            if (children is not JsonArray arr || arr.Count == 0)
                return new { Success = false, Error = "Provide text or childrenJson to append." };
            var doc = await client.PatchAsync($"/blocks/{r.BlockId}/children", new JsonObject { ["children"] = children });
            return new { Success = true, Result = (object)doc };
        });

    [Display(Name = NotionMethods.QueryDatabase)]
    [Description("Query a Notion database, optionally with a filter and sorts (raw Notion JSON).")]
    [Parameters("""{"type":"object","properties":{"databaseId":{"type":"string"},"filterJson":{"type":"string","description":"Raw Notion filter object as JSON"},"sortsJson":{"type":"string","description":"Raw Notion sorts array as JSON"},"pageSize":{"type":"integer","description":"Max results (default 25)"}},"required":["databaseId"]}""")]
    public Task<object> QueryDatabase(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var body = new JsonObject { ["page_size"] = r.PageSize ?? 25 };
            var filter = ParseOrNull(r.FilterJson);
            if (filter is not null) body["filter"] = filter;
            var sorts = ParseOrNull(r.SortsJson);
            if (sorts is not null) body["sorts"] = sorts;
            var doc = await client.PostAsync($"/databases/{r.DatabaseId}/query", body);
            return new { Success = true, Results = Prop(doc, "results") };
        });

    [Display(Name = NotionMethods.UpdatePage)]
    [Description("Update a Notion page's properties (raw Notion properties object as JSON).")]
    [Parameters("""{"type":"object","properties":{"pageId":{"type":"string"},"propertiesJson":{"type":"string","description":"Raw Notion properties object as JSON"}},"required":["pageId","propertiesJson"]}""")]
    public Task<object> UpdatePage(ServiceConfig config, ServiceRequest r) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var props = ParseOrNull(r.PropertiesJson)
                        ?? throw new InvalidOperationException("propertiesJson is required.");
            var doc = await client.PatchAsync($"/pages/{r.PageId}", new JsonObject { ["properties"] = props });
            return new { Success = true, Page = (object)doc };
        });

    private static NotionClient Client(ServiceConfig config) => new(config.AccessToken, config.NotionVersion);

    private static object Prop(JsonElement doc, string name) =>
        doc.ValueKind == JsonValueKind.Object && doc.TryGetProperty(name, out var el) ? el : doc;

    private async Task<object> Guard(Func<Task<object>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await _logger(LogLevel.Error, $"Notion operation failed: {ex.Message}", ex);
            return new { Success = false, Error = ex.Message };
        }
    }
}
