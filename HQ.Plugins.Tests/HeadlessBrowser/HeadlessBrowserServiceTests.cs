using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.HeadlessBrowser;
using HQ.Plugins.HeadlessBrowser.Models;
using Microsoft.Playwright;
using Moq;

namespace HQ.Plugins.Tests.HeadlessBrowser;

public class HeadlessBrowserServiceTests
{
    private readonly Mock<IBrowserClient> _mockClient;
    private readonly ServiceConfig _config;
    private readonly HeadlessBrowserService _service;

    public HeadlessBrowserServiceTests()
    {
        _mockClient = new Mock<IBrowserClient>();
        _config = new ServiceConfig
        {
            Name = "HeadlessBrowser",
            DefaultTimeoutMs = 30000
        };

        LogDelegate logger = (level, msg, ex) => Task.CompletedTask;
        _service = new HeadlessBrowserService(_mockClient.Object, _config, logger);
    }

    [Fact]
    public async Task ExecuteJavascript_WhenScriptThrowsPlaywrightException_ReturnsErrorResult()
    {
        var request = new ServiceRequest { Script = "throw new Error('test error')" };

        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ThrowsAsync(new PlaywrightException("Evaluation failed: Error: test error"));

        var result = await _service.ExecuteJavascript(_config, request);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("false", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test error", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteJavascript_WhenScriptSucceeds_ReturnsSuccessResult()
    {
        var request = new ServiceRequest { Script = "return 42" };
        var expected = new { Success = true, Result = (object)42 };

        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ReturnsAsync(new { Success = true, Result = (object)42 });

        var result = await _service.ExecuteJavascript(_config, request);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("true", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteJavascript_WhenScriptIsEmpty_ThrowsArgumentException()
    {
        var request = new ServiceRequest { Script = "" };

        await Assert.ThrowsAsync<ArgumentException>(() => _service.ExecuteJavascript(_config, request));
    }

    [Fact]
    public async Task GetPageContent_WhenEvaluateThrowsPlaywrightException_ReturnsErrorResult()
    {
        var request = new ServiceRequest { ContentType = "text" };

        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ThrowsAsync(new PlaywrightException("Page crashed"));

        var result = await _service.GetPageContent(_config, request);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("false", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Page crashed", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetInteractiveElements_WhenEvaluateThrowsPlaywrightException_ReturnsErrorResult()
    {
        var request = new ServiceRequest { ElementType = "all" };

        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ThrowsAsync(new PlaywrightException("Execution context was destroyed"));

        var result = await _service.GetInteractiveElements(_config, request);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("false", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Execution context was destroyed", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitForm_WhenEvaluateThrowsPlaywrightException_ReturnsErrorResult()
    {
        var request = new ServiceRequest { Selector = "#myform" };

        // SubmitForm now returns ValueTuple via WithDiffIfEnabled
        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<(bool, string, string, string)>>>()))
            .ThrowsAsync(new PlaywrightException("Element is not a form"));

        var result = await _service.SubmitForm(_config, request);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("false", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Element is not a form", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NavigateToUrl_WhenMissingBrowsers_ReturnsInstallMessage()
    {
        var request = new ServiceRequest { Url = "https://example.com" };

        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ThrowsAsync(new PlaywrightException("Executable doesn't exist at /path/chromium"));

        var result = await _service.NavigateToUrl(_config, request);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("false", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("install", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NavigateToUrl_WhenPageThrowsPlaywrightException_ReturnsErrorResult()
    {
        var request = new ServiceRequest { Url = "https://example.com" };

        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ThrowsAsync(new PlaywrightException("net::ERR_NAME_NOT_RESOLVED"));

        var result = await _service.NavigateToUrl(_config, request);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.Contains("false", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ERR_NAME_NOT_RESOLVED", json, StringComparison.OrdinalIgnoreCase);
    }
}
