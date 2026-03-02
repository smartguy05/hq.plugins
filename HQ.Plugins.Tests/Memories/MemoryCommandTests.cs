using HQ.Plugins.Memories;

namespace HQ.Plugins.Tests.Memories;

public class MemoryCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        // ChromaService has 6 annotated tool methods
        var command = new MemoryCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(6, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new MemoryCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function);
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Description));
        });
    }
}
