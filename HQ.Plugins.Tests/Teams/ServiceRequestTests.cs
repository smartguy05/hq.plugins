using HQ.Plugins.Teams.Models;

namespace HQ.Plugins.Tests.Teams;

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
        Assert.Null(request.TeamId);
        Assert.Null(request.ChannelId);
        Assert.Null(request.ChatId);
        Assert.Null(request.MessageText);
        Assert.Null(request.FileContent);
        Assert.Null(request.FileName);
        Assert.Null(request.FileType);
        Assert.Null(request.DriveItemId);
    }

    [Fact]
    public void ServiceRequest_ShouldSetMessageProperties()
    {
        var request = new ServiceRequest
        {
            Method = "send_teams_message",
            TeamId = "team-123",
            ChannelId = "channel-456",
            MessageText = "Hello Teams!"
        };

        Assert.Equal("send_teams_message", request.Method);
        Assert.Equal("team-123", request.TeamId);
        Assert.Equal("channel-456", request.ChannelId);
        Assert.Equal("Hello Teams!", request.MessageText);
    }

    [Fact]
    public void ServiceRequest_ShouldSetFileProperties()
    {
        var request = new ServiceRequest
        {
            Method = "send_teams_file",
            TeamId = "team-123",
            ChannelId = "channel-456",
            FileContent = "SGVsbG8gV29ybGQ=",
            FileName = "test.txt",
            FileType = "text/plain"
        };

        Assert.Equal("send_teams_file", request.Method);
        Assert.Equal("SGVsbG8gV29ybGQ=", request.FileContent);
        Assert.Equal("test.txt", request.FileName);
        Assert.Equal("text/plain", request.FileType);
    }

    [Fact]
    public void ServiceRequest_ShouldSetDownloadProperties()
    {
        var request = new ServiceRequest
        {
            Method = "download_teams_file",
            DriveItemId = "driveId/itemId"
        };

        Assert.Equal("download_teams_file", request.Method);
        Assert.Equal("driveId/itemId", request.DriveItemId);
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
