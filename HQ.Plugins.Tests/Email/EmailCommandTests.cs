using HQ.Plugins.Email;

namespace HQ.Plugins.Tests.Email;

public class EmailCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        // EmailService has 17 annotated tool methods
        var command = new EmailCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(17, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new EmailCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function);
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Function.Description));
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveTypeFunction()
    {
        var command = new EmailCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t => Assert.Equal("function", t.Type));
    }
}
