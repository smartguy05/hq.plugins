using HQ.Plugins.LinkedIn;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInCommandTests
{
    private readonly LinkedInCommand _command;

    public LinkedInCommandTests()
    {
        _command = new LinkedInCommand();
    }

    [Fact]
    public void Name_ReturnsLinkedIn()
    {
        Assert.Equal("LinkedIn", _command.Name);
    }

    [Fact]
    public void Description_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(_command.Description));
    }

    [Fact]
    public void GetToolDefinitions_ReturnsExactly9Tools()
    {
        var tools = _command.GetToolDefinitions();
        Assert.Equal(9, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveNames()
    {
        var tools = _command.GetToolDefinitions();
        Assert.All(tools, tool =>
        {
            Assert.NotNull(tool.Function);
            Assert.False(string.IsNullOrWhiteSpace(tool.Function.Name));
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveDescriptions()
    {
        var tools = _command.GetToolDefinitions();
        Assert.All(tools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Function.Description));
        });
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveParameters()
    {
        var tools = _command.GetToolDefinitions();
        Assert.All(tools, tool =>
        {
            Assert.NotNull(tool.Function.Parameters);
        });
    }

    [Theory]
    [InlineData("get_all_chats")]
    [InlineData("get_chat_messages")]
    [InlineData("get_user_profile")]
    [InlineData("create_post")]
    [InlineData("send_comment")]
    [InlineData("get_inmail_balance")]
    [InlineData("send_invitation")]
    [InlineData("send_message")]
    [InlineData("start_new_chat")]
    public void GetToolDefinitions_ContainsExpectedTool(string toolName)
    {
        var tools = _command.GetToolDefinitions();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }
}
