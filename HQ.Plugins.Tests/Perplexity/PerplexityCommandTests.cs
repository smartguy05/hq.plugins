using HQ.Plugins.Perplexity;
using HQ.Plugins.Perplexity.Models;

namespace HQ.Plugins.Tests.Perplexity;

public class PerplexityCommandTests
{
    // WP6B-15: [Injected] only hides a property from the LLM-visible tool schema — it does not
    // stop DeserializeArgs from binding a same-named key a model puts in its own tool-call JSON.
    // A ConversationId/RequestingService property on this args type is therefore always
    // attacker-settable (via prompt injection) no matter what attribute decorates it, and there is
    // no host-trusted per-call channel today to overwrite it (unlike organizationId). The only
    // reliable fix at the plugin layer is to not have the property at all, so no JSON key can ever
    // bind into a conversation-routing field.
    [Theory]
    [InlineData("ConversationId")]
    [InlineData("RequestingService")]
    public void PerplexityDeepResearchArgs_HasNoConversationRoutingProperty(string propertyName)
    {
        var property = typeof(PerplexityDeepResearchArgs).GetProperty(propertyName);
        Assert.Null(property);
    }

    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        var command = new PerplexityCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new PerplexityCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function);
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Description));
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveParameters()
    {
        var command = new PerplexityCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function.Parameters);
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsAreFunction()
    {
        var command = new PerplexityCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.Equal("function", t.Type);
        });
    }

    [Theory]
    [InlineData("perplexity_search")]
    [InlineData("perplexity_deep_research")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var command = new PerplexityCommand();
        var tools = command.GetToolDefinitions();
        var toolNames = tools.Select(t => t.Function.Name).ToList();
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    public void Name_ReturnsPerplexityResearch()
    {
        var command = new PerplexityCommand();
        Assert.Equal("Perplexity Research", command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var command = new PerplexityCommand();
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
