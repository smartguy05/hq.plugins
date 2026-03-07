using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Memories.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("ChromaDB server URL for memory storage, e.g. http://127.0.0.1:8000")]
    public string ChromaUrl { get; set; }

    [Tooltip("ChromaDB collection name for this agent's memories")]
    public string DefaultCollectionName { get; set; }

    [LlmProviderKey]
    public string OpenAiApiKey { get; set; }

    [LlmProviderUrl]
    public string OpenAiUrl { get; set; }

    [LlmProviderModel]
    public string EmbeddingModel { get; set; }

    [Hidden]
    [Tooltip("Agent ID that owns this config. Auto-populated by the system.")]
    public string AgentId { get; set; }
}
