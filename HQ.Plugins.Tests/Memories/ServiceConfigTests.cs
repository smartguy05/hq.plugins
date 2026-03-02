using System.Text.Json;
using HQ.Plugins.Memories.Models;

namespace HQ.Plugins.Tests.Memories;

public class ServiceConfigTests
{
    [Fact]
    public void Deserialize_WithAgentId_SetsProperty()
    {
        var agentId = Guid.NewGuid().ToString();
        var json = JsonSerializer.Serialize(new
        {
            chromaUrl = "http://localhost:8000",
            defaultCollectionName = "test",
            openAiApiKey = "key",
            agentId = agentId
        });

        var config = JsonSerializer.Deserialize<ServiceConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.Equal(agentId, config.AgentId);
    }

    [Fact]
    public void Deserialize_WithoutAgentId_AgentIdIsNull()
    {
        var json = JsonSerializer.Serialize(new
        {
            chromaUrl = "http://localhost:8000",
            defaultCollectionName = "test",
            openAiApiKey = "key"
        });

        var config = JsonSerializer.Deserialize<ServiceConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.Null(config.AgentId);
    }

    [Fact]
    public void Deserialize_WithAgentIdInFullConfig_PreservesAllFields()
    {
        var agentId = Guid.NewGuid().ToString();
        var json = $@"{{
            ""chromaUrl"": ""http://localhost:8000"",
            ""defaultCollectionName"": ""memories"",
            ""openAiApiKey"": ""sk-test"",
            ""openAiUrl"": ""http://custom-endpoint"",
            ""embeddingModel"": ""custom-model"",
            ""agentId"": ""{agentId}"",
            ""agents"": [{{""name"": ""test""}}],
            ""redisConversationSubject"": ""chat""
        }}";

        var config = JsonSerializer.Deserialize<ServiceConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.Equal("http://localhost:8000", config.ChromaUrl);
        Assert.Equal("memories", config.DefaultCollectionName);
        Assert.Equal("sk-test", config.OpenAiApiKey);
        Assert.Equal("http://custom-endpoint", config.OpenAiUrl);
        Assert.Equal("custom-model", config.EmbeddingModel);
        Assert.Equal(agentId, config.AgentId);
    }
}
