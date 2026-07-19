using HQ.Plugins.Teams.Models;

namespace HQ.Plugins.Tests.Teams;

/// <summary>
/// Post-migration, <see cref="ServiceRequest"/> carries only orchestrator routing fields; the
/// per-tool LLM arguments (team/channel/message/file fields) now live on dedicated args records in
/// <c>ToolArgs.cs</c>. These tests preserve the original intent — verifying the DTOs round-trip —
/// against the new shapes.
/// </summary>
public class ServiceRequestTests
{
    [Fact]
    public void ServiceRequest_ShouldInitializeWithNullValues()
    {
        var request = new ServiceRequest();
        Assert.Null(request.Method);
        Assert.Null(request.ToolCallId);
        Assert.Null(request.RequestingService);
        Assert.Null(request.ConfirmationId);
    }

    [Fact]
    public void SendTeamsMessageArgs_ShouldSetMessageProperties()
    {
        var request = new ServiceRequest { Method = "send_teams_message" };
        var args = new SendTeamsMessageArgs
        {
            TeamId = "team-123",
            ChannelId = "channel-456",
            MessageText = "Hello Teams!"
        };

        Assert.Equal("send_teams_message", request.Method);
        Assert.Equal("team-123", args.TeamId);
        Assert.Equal("channel-456", args.ChannelId);
        Assert.Equal("Hello Teams!", args.MessageText);
    }

    [Fact]
    public void SendTeamsFileArgs_ShouldSetFileProperties()
    {
        var args = new SendTeamsFileArgs
        {
            TeamId = "team-123",
            ChannelId = "channel-456",
            FileContent = "SGVsbG8gV29ybGQ=",
            FileName = "test.txt",
            FileType = "text/plain"
        };

        Assert.Equal("team-123", args.TeamId);
        Assert.Equal("channel-456", args.ChannelId);
        Assert.Equal("SGVsbG8gV29ybGQ=", args.FileContent);
        Assert.Equal("test.txt", args.FileName);
        Assert.Equal("text/plain", args.FileType);
    }

    [Fact]
    public void ListTeamsChannelsArgs_ShouldSetTeamId()
    {
        var args = new ListTeamsChannelsArgs { TeamId = "team-123" };
        Assert.Equal("team-123", args.TeamId);
    }

    [Fact]
    public void DownloadTeamsFileArgs_ShouldSetDownloadProperties()
    {
        var args = new DownloadTeamsFileArgs { DriveItemId = "driveId/itemId" };
        Assert.Equal("driveId/itemId", args.DriveItemId);
    }

    [Fact]
    public void ServiceRequest_ShouldSetInterfaceProperties()
    {
        var request = new ServiceRequest
        {
            ToolCallId = "call-789",
            RequestingService = "orchestrator",
            ConfirmationId = "confirm-abc"
        };

        Assert.Equal("call-789", request.ToolCallId);
        Assert.Equal("orchestrator", request.RequestingService);
        Assert.Equal("confirm-abc", request.ConfirmationId);
    }
}
