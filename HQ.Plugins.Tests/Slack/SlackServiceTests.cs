using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Slack;
using HQ.Plugins.Slack.Models;
using Moq;
using SlackNet;
using SlackNet.SocketMode;
using SlackNet.WebApi;

namespace HQ.Plugins.Tests.Slack;

public class SlackServiceTests
{
    private readonly Mock<ISlackApiClient> _mockApiClient;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly List<(LogLevel Level, string Message)> _logEntries;
    private readonly LogDelegate _logger;
    private readonly ServiceConfig _config;

    public SlackServiceTests()
    {
        _mockApiClient = new Mock<ISlackApiClient>();
        _mockNotificationService = new Mock<INotificationService>();
        _logEntries = new List<(LogLevel, string)>();
        _logger = (level, message, exception) =>
        {
            _logEntries.Add((level, message));
            return Task.CompletedTask;
        };
        _config = new ServiceConfig
        {
            Name = "test-slack",
            Description = "Test Slack config",
            BotToken = "xoxb-test-token",
            AppLevelToken = "xapp-test-token",
            AiPlugin = "TestAi",
            NotificationChannelId = "C12345"
        };
    }

    [Fact]
    public async Task Connect_WithSuccess_LogsConnected()
    {
        var mockSocketClient = new Mock<ISlackSocketModeClient>();
        mockSocketClient.Setup(c => c.Connect(It.IsAny<SocketModeConnectionOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new SlackService(_mockApiClient.Object, _logger, _config, _mockNotificationService.Object,
            (id, val) => ValueTask.FromResult<object>(null));

        await service.Connect(mockSocketClient.Object);

        Assert.Contains(_logEntries, e => e.Level == LogLevel.Info && e.Message.Contains("connected"));
    }

    [Fact]
    public async Task Connect_WithAuthError_StopsRetryingImmediately()
    {
        var mockSocketClient = new Mock<ISlackSocketModeClient>();
        var callCount = 0;
        mockSocketClient.Setup(c => c.Connect(It.IsAny<SocketModeConnectionOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ThrowsAsync(new SlackException(new ErrorResponse { Error = "not_authed" }));

        var service = new SlackService(_mockApiClient.Object, _logger, _config, _mockNotificationService.Object,
            (id, val) => ValueTask.FromResult<object>(null));

        await service.Connect(mockSocketClient.Object);

        Assert.Equal(1, callCount);
        Assert.Contains(_logEntries, e => e.Level == LogLevel.Error && e.Message.Contains("not_authed"));
        Assert.DoesNotContain(_logEntries, e => e.Message.Contains("retrying"));
    }

    [Fact]
    public async Task Connect_WithInvalidAuthError_StopsRetryingImmediately()
    {
        var mockSocketClient = new Mock<ISlackSocketModeClient>();
        var callCount = 0;
        mockSocketClient.Setup(c => c.Connect(It.IsAny<SocketModeConnectionOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ThrowsAsync(new SlackException(new ErrorResponse { Error = "invalid_auth" }));

        var service = new SlackService(_mockApiClient.Object, _logger, _config, _mockNotificationService.Object,
            (id, val) => ValueTask.FromResult<object>(null));

        await service.Connect(mockSocketClient.Object);

        Assert.Equal(1, callCount);
        Assert.Contains(_logEntries, e => e.Level == LogLevel.Error && e.Message.Contains("invalid_auth"));
    }

    [Fact]
    public async Task Connect_WithTokenRevokedError_StopsRetryingImmediately()
    {
        var mockSocketClient = new Mock<ISlackSocketModeClient>();
        var callCount = 0;
        mockSocketClient.Setup(c => c.Connect(It.IsAny<SocketModeConnectionOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ThrowsAsync(new SlackException(new ErrorResponse { Error = "token_revoked" }));

        var service = new SlackService(_mockApiClient.Object, _logger, _config, _mockNotificationService.Object,
            (id, val) => ValueTask.FromResult<object>(null));

        await service.Connect(mockSocketClient.Object);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task Connect_WithTransientError_RetriesThenStops()
    {
        var mockSocketClient = new Mock<ISlackSocketModeClient>();
        var callCount = 0;
        mockSocketClient.Setup(c => c.Connect(It.IsAny<SocketModeConnectionOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => callCount++)
            .ThrowsAsync(new Exception("connection_timeout"));

        var service = new SlackService(_mockApiClient.Object, _logger, _config, _mockNotificationService.Object,
            (id, val) => ValueTask.FromResult<object>(null));

        await service.Connect(mockSocketClient.Object);

        // Should retry a limited number of times, not forever
        Assert.True(callCount <= 5, $"Expected at most 5 attempts but got {callCount}");
        Assert.True(callCount > 1, $"Expected more than 1 attempt but got {callCount}");
        Assert.Contains(_logEntries, e => e.Message.Contains("retrying"));
    }

    [Fact]
    public async Task Connect_WithTransientErrorThenSuccess_Connects()
    {
        var mockSocketClient = new Mock<ISlackSocketModeClient>();
        var callCount = 0;
        mockSocketClient.Setup(c => c.Connect(It.IsAny<SocketModeConnectionOptions>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount < 3)
                    throw new Exception("connection_timeout");
                return Task.CompletedTask;
            });

        var service = new SlackService(_mockApiClient.Object, _logger, _config, _mockNotificationService.Object,
            (id, val) => ValueTask.FromResult<object>(null));

        await service.Connect(mockSocketClient.Object);

        Assert.Equal(3, callCount);
        Assert.Contains(_logEntries, e => e.Level == LogLevel.Info && e.Message.Contains("connected"));
    }
}
