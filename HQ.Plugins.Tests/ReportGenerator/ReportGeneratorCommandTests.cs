using HQ.Plugins.ReportGenerator;

namespace HQ.Plugins.Tests.ReportGenerator;

public class ReportGeneratorCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        var command = new ReportGeneratorCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(3, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new ReportGeneratorCommand();
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
        var command = new ReportGeneratorCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function.Parameters);
        });
    }

    [Theory]
    [InlineData("generate_report")]
    [InlineData("list_reports")]
    [InlineData("get_report")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var command = new ReportGeneratorCommand();
        var tools = command.GetToolDefinitions();
        var toolNames = tools.Select(t => t.Function.Name).ToList();
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    public void Name_ReturnsReportGenerator()
    {
        var command = new ReportGeneratorCommand();
        Assert.Equal("Report Generator", command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var command = new ReportGeneratorCommand();
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
