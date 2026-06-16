namespace HQ.Plugins.Notion;

/// <summary>Tool-name constants. Each must match a [Display(Name=...)] on NotionService.</summary>
public static class NotionMethods
{
    public const string Search = "notion_search";
    public const string GetPage = "notion_get_page";
    public const string CreatePage = "notion_create_page";
    public const string AppendBlock = "notion_append_block";
    public const string QueryDatabase = "notion_query_database";
    public const string UpdatePage = "notion_update_page";
}
