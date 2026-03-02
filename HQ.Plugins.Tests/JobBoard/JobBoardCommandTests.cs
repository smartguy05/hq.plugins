using HQ.Plugins.JobBoard;

namespace HQ.Plugins.Tests.JobBoard;

public class JobBoardCommandTests
{
    [Fact]
    public void GetToolDefinitions_ReturnsExpectedToolCount()
    {
        var command = new JobBoardCommand();
        var tools = command.GetToolDefinitions();
        Assert.Equal(6, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var command = new JobBoardCommand();
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
        var command = new JobBoardCommand();
        var tools = command.GetToolDefinitions();
        Assert.All(tools, t =>
        {
            Assert.NotNull(t.Function.Parameters);
        });
    }

    [Theory]
    [InlineData("search_jobs")]
    [InlineData("get_job_details")]
    [InlineData("track_application")]
    [InlineData("update_application")]
    [InlineData("get_applications")]
    [InlineData("get_job_summary")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var command = new JobBoardCommand();
        var tools = command.GetToolDefinitions();
        var toolNames = tools.Select(t => t.Function.Name).ToList();
        Assert.Contains(toolName, toolNames);
    }

    [Fact]
    public void Name_ReturnsJobBoard()
    {
        var command = new JobBoardCommand();
        Assert.Equal("Job Board", command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        var command = new JobBoardCommand();
        Assert.False(string.IsNullOrWhiteSpace(command.Description));
    }
}
