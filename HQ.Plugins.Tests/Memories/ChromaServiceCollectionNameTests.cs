using HQ.Plugins.Memories;
using HQ.Plugins.Memories.Models;

namespace HQ.Plugins.Tests.Memories;

public class ChromaServiceCollectionNameTests
{
    [Fact]
    public void GetCollectionName_WithAgentId_ReturnsAgentScopedName()
    {
        var agentId = Guid.NewGuid().ToString();
        var config = new ServiceConfig
        {
            DefaultCollectionName = "shared-memories",
            AgentId = agentId
        };

        var result = ChromaService.GetCollectionName(config);

        Assert.Equal($"agent-{agentId}-memories", result);
    }

    [Fact]
    public void GetCollectionName_WithoutAgentId_ReturnsDefaultCollectionName()
    {
        var config = new ServiceConfig
        {
            DefaultCollectionName = "shared-memories",
            AgentId = null
        };

        var result = ChromaService.GetCollectionName(config);

        Assert.Equal("shared-memories", result);
    }

    [Fact]
    public void GetCollectionName_EmptyAgentId_ReturnsDefaultCollectionName()
    {
        var config = new ServiceConfig
        {
            DefaultCollectionName = "shared-memories",
            AgentId = ""
        };

        var result = ChromaService.GetCollectionName(config);

        Assert.Equal("shared-memories", result);
    }

    [Fact]
    public void GetCollectionName_AgentId_ProducesConsistentNames()
    {
        var agentId = "12345678-1234-1234-1234-123456789abc";
        var config = new ServiceConfig
        {
            DefaultCollectionName = "default",
            AgentId = agentId
        };

        var result1 = ChromaService.GetCollectionName(config);
        var result2 = ChromaService.GetCollectionName(config);

        Assert.Equal(result1, result2);
        Assert.StartsWith("agent-", result1);
        Assert.EndsWith("-memories", result1);
    }
}
