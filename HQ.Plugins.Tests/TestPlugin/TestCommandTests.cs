using HQ.Models;
using HQ.Models.Interfaces;
using Test_Plugin;

namespace HQ.Plugins.Tests.TestPlugin;

public class TestCommandTests
{
    [Fact]
    public void Name_ReturnsExpected()
    {
        var command = new TestCommand();
        Assert.Equal("TEST", command.Name);
    }

    [Fact]
    public void Description_ReturnsExpected()
    {
        var command = new TestCommand();
        Assert.Equal("Displays hello message.", command.Description);
    }

    [Fact]
    public async Task Execute_WithNullArgs_ReturnsZero()
    {
        var command = new TestCommand();
        var request = new OrchestratorRequest
        {
            Service = "TEST",
            ServiceRequest = null,
            ToolCallId = "test-call-1"
        };
        var config = "{\"TestName\":\"hello\",\"TestName2\":\"world\"}";

        var result = await command.Execute(request, config, [], (_, _, _) => Task.CompletedTask, null);

        Assert.Equal(0, result);
    }

    [Fact]
    public async Task Execute_WithConfig_ReturnsZero()
    {
        var command = new TestCommand();
        var request = new OrchestratorRequest
        {
            Service = "TEST",
            ServiceRequest = null,
            ToolCallId = "test-call-2"
        };
        var config = "{\"TestName\":\"alpha\",\"TestName2\":\"beta\"}";

        var result = await command.Execute(request, config, [], (_, _, _) => Task.CompletedTask, null);

        Assert.Equal(0, result);
    }

    [Fact]
    public void GetToolDefinitions_ReturnsEmptyList()
    {
        ICommand command = new TestCommand();
        var tools = command.GetToolDefinitions();
        Assert.Empty(tools);
    }

    [Fact]
    public void GetConfigTemplate_ReturnsFallbackJson()
    {
        ICommand command = new TestCommand();
        var template = command.GetConfigTemplate();
        Assert.Equal("{}", template);
    }
}
