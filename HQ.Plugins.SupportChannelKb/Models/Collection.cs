namespace HQ.Plugins.SupportChannelKb.Models;

// WP6B-12: the upstream KB service's GET /collections response includes a per-collection
// api_key. Nothing in this plugin reads it (auth uses ServiceConfig.DefaultChannelApiKey), so it
// must NOT be modeled here — this DTO is returned verbatim as the get_support_channel_collections
// tool result, which is serialized into the LLM conversation and the persisted trace/debug log.
// Deliberately do not add an ApiKey/api_key-bound property back to this class.
public class Collection
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string Created { get; set; }
}