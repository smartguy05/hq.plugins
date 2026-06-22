using HQ.Plugins.LinkedIn;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInCommandTests
{
    private readonly LinkedInCommand _command = new();

    [Fact]
    public void Name_ReturnsLinkedIn() => Assert.Equal("LinkedIn", _command.Name);

    [Fact]
    public void Description_IsNotEmpty() => Assert.False(string.IsNullOrWhiteSpace(_command.Description));

    [Fact]
    public void GetToolDefinitions_ReturnsAllTools()
    {
        // 9 original tools preserved + react_to_post + 4 search/enrichment tools.
        var tools = _command.GetToolDefinitions();
        Assert.Equal(14, tools.Count);
    }

    [Fact]
    public void GetToolDefinitions_AllToolsHaveNameDescriptionAndParameters()
    {
        var tools = _command.GetToolDefinitions();
        Assert.All(tools, tool =>
        {
            Assert.NotNull(tool.Function);
            Assert.False(string.IsNullOrWhiteSpace(tool.Function.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Function.Description));
            Assert.NotNull(tool.Function.Parameters);
        });
    }

    // Every tool the original (Relevance AI) plugin exposed must still be present.
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
    public void GetToolDefinitions_PreservesOriginalTools(string toolName)
    {
        var tools = _command.GetToolDefinitions();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }

    // New search/enrichment + engagement tools added on top.
    [Theory]
    [InlineData("react_to_post")]
    [InlineData("search_people")]
    [InlineData("lookup_person")]
    [InlineData("search_companies")]
    [InlineData("lookup_company")]
    public void GetToolDefinitions_AddsNewTools(string toolName)
    {
        var tools = _command.GetToolDefinitions();
        Assert.Contains(tools, t => t.Function.Name == toolName);
    }
}
