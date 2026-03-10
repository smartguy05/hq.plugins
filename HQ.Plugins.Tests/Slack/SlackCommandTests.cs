using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.Slack;
using HQ.Plugins.Slack.Models;
using Moq;

namespace HQ.Plugins.Tests.Slack;

public class SlackCommandTests
{
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly List<(LogLevel Level, string Message)> _logEntries;
    private readonly LogDelegate _logger;

    public SlackCommandTests()
    {
        _mockNotificationService = new Mock<INotificationService>();
        _logEntries = new List<(LogLevel, string)>();
        _logger = (level, message, exception) =>
        {
            _logEntries.Add((level, message));
            return Task.CompletedTask;
        };
    }

    [Fact]
    public async Task Initialize_WithMissingBotToken_LogsWarningAndReturnsGracefully()
    {
        var config = new ServiceConfig
        {
            Name = "test-slack",
            Description = "Test",
            BotToken = null,
            AppLevelToken = "xapp-test"
        };
        var configString = JsonSerializer.Serialize(config);

        var command = new SlackCommand();
        var result = await command.Initialize(configString, _logger, _mockNotificationService.Object);

        Assert.Null(result);
        Assert.Contains(_logEntries, e => e.Level == LogLevel.Warning && e.Message.Contains("BotToken"));
    }

    [Fact]
    public async Task Initialize_WithEmptyBotToken_LogsWarningAndReturnsGracefully()
    {
        var config = new ServiceConfig
        {
            Name = "test-slack",
            Description = "Test",
            BotToken = "",
            AppLevelToken = "xapp-test"
        };
        var configString = JsonSerializer.Serialize(config);

        var command = new SlackCommand();
        var result = await command.Initialize(configString, _logger, _mockNotificationService.Object);

        Assert.Null(result);
        Assert.Contains(_logEntries, e => e.Level == LogLevel.Warning && e.Message.Contains("BotToken"));
    }

    [Fact]
    public async Task Initialize_WithMissingAppLevelToken_LogsWarningAndReturnsGracefully()
    {
        var config = new ServiceConfig
        {
            Name = "test-slack",
            Description = "Test",
            BotToken = "xoxb-test",
            AppLevelToken = null
        };
        var configString = JsonSerializer.Serialize(config);

        var command = new SlackCommand();
        var result = await command.Initialize(configString, _logger, _mockNotificationService.Object);

        Assert.Null(result);
        Assert.Contains(_logEntries, e => e.Level == LogLevel.Warning && e.Message.Contains("AppLevelToken"));
    }

    [Fact]
    public async Task Initialize_WithBothTokensMissing_LogsWarningAndReturnsGracefully()
    {
        var config = new ServiceConfig
        {
            Name = "test-slack",
            Description = "Test",
            BotToken = null,
            AppLevelToken = null
        };
        var configString = JsonSerializer.Serialize(config);

        var command = new SlackCommand();
        var result = await command.Initialize(configString, _logger, _mockNotificationService.Object);

        Assert.Null(result);
        Assert.Contains(_logEntries, e => e.Level == LogLevel.Warning);
    }
}
