using HQ.Models.Interfaces;

namespace HQ.Plugins.Perplexity.Models;

public class ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Injected by the host — identifies the conversation, used to deliver async results back.
    public string ConversationId { get; set; }

    // Tool parameters
    public string Query { get; set; }

    // Maps to search_recency_filter: day | week | month | year
    public string Recency { get; set; }

    // Maps to search_domain_filter; merged with config.DefaultDomainFilters
    public List<string> DomainFilters { get; set; }

    // Per-call model override for perplexity_search
    public string Model { get; set; }
}
