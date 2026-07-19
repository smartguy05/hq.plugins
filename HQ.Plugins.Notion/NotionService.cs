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
    private readonly HttpMessageHandler _handler;

    public NotionService(LogDelegate logger) => _logger = logger;

    // Test seam: lets a fake HttpMessageHandler be injected without hitting the network.
    internal NotionService(LogDelegate logger, HttpMessageHandler handler)
    {
        _logger = logger;
        _handler = handler;
    }

    private static readonly string ApiHost = new Uri(NotionClient.BaseUrl).Host;

    // Wrap third-party human-authored payloads (raw Notion JSON — page/database content typed by
    // people) as ONE Untrusted envelope so the host can classify it for prompt-injection before
    // the LLM sees it. Ids/counts/status flags in the wrapper stay raw.
    private static object AsUntrusted(string content, string provenance, string source) =>
        string.IsNullOrEmpty(content) ? content : new HQ.Models.Safety.Untrusted<string>(content, provenance, source);

    private static object WrapRaw(object json, string provenance, string source) =>
        json is JsonElement el && el.ValueKind != JsonValueKind.Undefined
            ? AsUntrusted(el.GetRawText(), provenance, source)
            : json;

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
    [Parameters(typeof(SearchArgs))]
    public Task<object> Search(ServiceConfig config, SearchArgs request) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var body = new JsonObject { ["query"] = request.Query ?? "", ["page_size"] = request.PageSize ?? 25 };
            if (!string.IsNullOrWhiteSpace(request.FilterType))
                body["filter"] = new JsonObject { ["property"] = "object", ["value"] = request.FilterType };
            var doc = await client.PostAsync("/search", body);
            return new { Success = true, Results = WrapRaw(Prop(doc, "results"), "notion-search-results", ApiHost) };
        });

    [Display(Name = NotionMethods.GetPage)]
    [Description("Get a Notion page's properties by id.")]
    [Parameters(typeof(GetPageArgs))]
    public Task<object> GetPage(ServiceConfig config, GetPageArgs request) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var doc = await client.GetAsync($"/pages/{request.PageId}");
            return new { Success = true, Page = WrapRaw(doc, "notion-page-content", request.PageId) };
        });

    [Display(Name = NotionMethods.CreatePage)]
    [Description("Create a page under a parent page or database. For a page parent, pass a title; for a database parent, pass propertiesJson matching the database schema. Optional text becomes paragraph blocks.")]
    [Parameters(typeof(CreatePageArgs))]
    public Task<object> CreatePage(ServiceConfig config, CreatePageArgs request) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var isDb = string.Equals(request.ParentType, "database", StringComparison.OrdinalIgnoreCase);
            var body = new JsonObject
            {
                ["parent"] = new JsonObject { [isDb ? "database_id" : "page_id"] = request.ParentId }
            };

            var props = ParseOrNull(request.PropertiesJson);
            if (props is not null) body["properties"] = props;
            else body["properties"] = new JsonObject { ["title"] = new JsonObject { ["title"] = RichText(request.Title) } };

            var children = ParseOrNull(request.ChildrenJson) ?? ParagraphBlocks(request.Text);
            if (children is JsonArray arr && arr.Count > 0) body["children"] = children;

            var doc = await client.PostAsync("/pages", body);
            return new { Success = true, Page = WrapRaw(doc, "notion-page-content", request.ParentId) };
        });

    [Display(Name = NotionMethods.AppendBlock)]
    [Description("Append content blocks to a page or block. Provide text (becomes paragraphs) or raw childrenJson.")]
    [Parameters(typeof(AppendBlockArgs))]
    public Task<object> AppendBlock(ServiceConfig config, AppendBlockArgs request) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var children = ParseOrNull(request.ChildrenJson) ?? ParagraphBlocks(request.Text);
            if (children is not JsonArray arr || arr.Count == 0)
                return new { Success = false, Error = "Provide text or childrenJson to append." };
            var doc = await client.PatchAsync($"/blocks/{request.BlockId}/children", new JsonObject { ["children"] = children });
            return new { Success = true, Result = WrapRaw(doc, "notion-block-content", request.BlockId) };
        });

    [Display(Name = NotionMethods.QueryDatabase)]
    [Description("Query a Notion database, optionally with a filter and sorts (raw Notion JSON).")]
    [Parameters(typeof(QueryDatabaseArgs))]
    public Task<object> QueryDatabase(ServiceConfig config, QueryDatabaseArgs request) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var body = new JsonObject { ["page_size"] = request.PageSize ?? 25 };
            var filter = ParseOrNull(request.FilterJson);
            if (filter is not null) body["filter"] = filter;
            var sorts = ParseOrNull(request.SortsJson);
            if (sorts is not null) body["sorts"] = sorts;
            var doc = await client.PostAsync($"/databases/{request.DatabaseId}/query", body);
            return new { Success = true, Results = WrapRaw(Prop(doc, "results"), "notion-database-results", request.DatabaseId) };
        });

    [Display(Name = NotionMethods.UpdatePage)]
    [Description("Update a Notion page's properties (raw Notion properties object as JSON).")]
    [Parameters(typeof(UpdatePageArgs))]
    public Task<object> UpdatePage(ServiceConfig config, UpdatePageArgs request) =>
        Guard(async () =>
        {
            using var client = Client(config);
            var props = ParseOrNull(request.PropertiesJson)
                        ?? throw new InvalidOperationException("propertiesJson is required.");
            var doc = await client.PatchAsync($"/pages/{request.PageId}", new JsonObject { ["properties"] = props });
            return new { Success = true, Page = WrapRaw(doc, "notion-page-content", request.PageId) };
        });

    private NotionClient Client(ServiceConfig config) => new(config.AccessToken, config.NotionVersion, _handler);

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
