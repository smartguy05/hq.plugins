using HQ.Models.Interfaces;

namespace HQ.Plugins.WebReader.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // The page to read.
    public string Url { get; set; }

    // read_page: cap the returned markdown length (overrides config default).
    public int? MaxLength { get; set; }

    // search_page: text to find within the page.
    public string Query { get; set; }

    // search_page: characters of surrounding context per match.
    public int? ContextChars { get; set; }

    // extract_links: only return links whose text or href contains this substring.
    public string Filter { get; set; }
}
