using HQ.Plugins.Perplexity;

namespace HQ.Plugins.Tests.Perplexity;

public class PerplexityCommandTests
{
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
