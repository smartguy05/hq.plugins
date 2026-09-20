using System.Reflection;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Plugins.HeadlessBrowser;
using HQ.Plugins.HeadlessBrowser.Models;
using Microsoft.Playwright;
using Moq;

namespace HQ.Plugins.Tests.HeadlessBrowser;

/// <summary>
/// WP6A-1 (Critical): HeadlessBrowser had no scheme/host validation before navigating, so a
/// prompt-injected "navigate to file:///app/dpkeys/" (or an internal http:// SSRF target) reached
/// Playwright unchecked. These tests exercise the guard through the injected <see cref="IBrowserClient"/>
/// seam — no real browser is launched, per the cluster ruling that non-browser assertions stay unit
/// tests and any real-Chromium coverage is [Trait("Category","Integration")].
/// </summary>
public class HeadlessBrowserUrlGuardTests
{
    private readonly Mock<IBrowserClient> _mockClient;
    private readonly ServiceConfig _config;
    private readonly HeadlessBrowserService _service;

    public HeadlessBrowserUrlGuardTests()
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

    public static IEnumerable<object[]> BlockedUrls => new[]
    {
        new object[] { "file:///app/dpkeys/" },
        new object[] { "file:///etc/passwd" },
        new object[] { "view-source:https://example.com" },
        new object[] { "http://127.0.0.1:8080/" },
        new object[] { "http://169.254.169.254/latest/meta-data/" },
        new object[] { "http://10.0.0.5/" },
        new object[] { "http://192.168.1.1/" },
        new object[] { "http://hq-postgres:5432/" },
        new object[] { "http://hq-chromadb:8000/api/v1/collections" },
        new object[] { "http://localhost/" }
    };

    [Theory]
    [MemberData(nameof(BlockedUrls))]
    public async Task NavigateToUrl_WithDisallowedTarget_BlocksAndNeverTouchesBrowser(string url)
    {
        var result = await _service.NavigateToUrl(_config, new NavigateToUrlArgs { Url = url });

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"Success\":false", json);

        _mockClient.Verify(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()), Times.Never);
    }

    [Fact]
    public async Task NavigateToUrl_WithOrdinaryHttpsUrl_IsAllowedThroughToTheBrowser()
    {
        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ReturnsAsync(new { Success = true });

        var result = await _service.NavigateToUrl(_config, new NavigateToUrlArgs { Url = "https://example.com" });

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"Success\":true", json);
        _mockClient.Verify(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()), Times.Once);
    }

    [Fact]
    public async Task NavigateToUrl_ConsultsTheInjectedUrlValidatorSeam()
    {
        string seenUrl = null;
        _service.UrlValidator = url =>
        {
            seenUrl = url;
            return (true, null);
        };
        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .ReturnsAsync(new { Success = true });

        await _service.NavigateToUrl(_config, new NavigateToUrlArgs { Url = "https://example.com/path" });

        Assert.Equal("https://example.com/path", seenUrl);
    }

    [Fact]
    public async Task NavigateToUrl_WhenInjectedValidatorRejects_NeverCallsBrowserEvenForAnAllowedLookingUrl()
    {
        // Proves the gate is the seam's decision, not a hard-coded scheme/host list re-implemented
        // in the service — a benign-looking URL is still blocked when the seam says no.
        _service.UrlValidator = _ => (false, "blocked by test seam");

        var result = await _service.NavigateToUrl(_config, new NavigateToUrlArgs { Url = "https://example.com" });

        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"Success\":false", json);
        Assert.Contains("blocked by test seam", json);
        _mockClient.Verify(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()), Times.Never);
    }

    [Theory]
    [InlineData("file:///app/dpkeys/", false)]
    [InlineData("http://hq-postgres:5432/", false)]
    [InlineData("http://127.0.0.1/", false)]
    [InlineData("https://example.com/", true)]
    public void BrowserClient_IsRequestNavigable_MatchesUrlGuardForRouteFilter(string url, bool expected)
    {
        Assert.Equal(expected, HQ.Plugins.HeadlessBrowser.BrowserClient.IsRequestNavigable(url));
    }

    [Fact]
    public void ExecuteJavascript_RequiresConfirmation()
    {
        var method = typeof(HeadlessBrowserService).GetMethod(nameof(HeadlessBrowserService.ExecuteJavascript));
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<SupportsConfirmationAttribute>());
    }

    // WP6A-2 (Medium) verifier follow-up: the bare [SupportsConfirmation] attribute above is
    // inert unless ServiceConfig.RequiresConfirmation actually serializes to true AND the plugin
    // implements the confirmation protocol itself (ConfirmationGate defers the first-call gate to
    // the plugin for tools it recognizes as self-gating, exactly like HQ.Plugins.Email). These
    // tests exercise that protocol end-to-end through the injected INotificationService seam
    // instead of just reflecting on the attribute.
    [Fact]
    public void ServiceConfig_RequiresConfirmation_DefaultsTrue()
    {
        Assert.True(new ServiceConfig().RequiresConfirmation);
    }

    [Fact]
    public async Task ExecuteJavascript_WithNoConfirmationId_RequestsConfirmationAndNeverRunsScript()
    {
        var mockNotification = new Mock<INotificationService>();
        mockNotification
            .Setup(n => n.RequestConfirmation(
                It.IsAny<string>(), It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()))
            .ReturnsAsync(new { Success = true, AwaitingConfirmation = true });
        LogDelegate logger = (level, msg, ex) => Task.CompletedTask;
        var service = new HeadlessBrowserService(_mockClient.Object, _config, logger, mockNotification.Object);

        var request = new ExecuteJavascriptArgs { Script = "return document.title" };
        var result = await service.ExecuteJavascript(_config, request);

        mockNotification.Verify(n => n.RequestConfirmation(
            "HQ.Plugins.HeadlessBrowser",
            It.Is<Confirmation>(c => c.ConfirmationMessage.Contains("run this JavaScript")),
            request), Times.Once);
        _mockClient.Verify(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJavascript_WithInvalidConfirmationId_ReturnsErrorAndNeverRunsScript()
    {
        var confirmId = Guid.NewGuid();
        var mockNotification = new Mock<INotificationService>();
        Confirmation outConf = null;
        mockNotification.Setup(n => n.DoesConfirmationExist(confirmId, out outConf)).Returns(false);
        LogDelegate logger = (level, msg, ex) => Task.CompletedTask;
        var service = new HeadlessBrowserService(_mockClient.Object, _config, logger, mockNotification.Object);

        var request = new ExecuteJavascriptArgs { Script = "return 1", ConfirmationId = confirmId.ToString() };
        var result = await service.ExecuteJavascript(_config, request);

        var successProp = result.GetType().GetProperty("Success");
        Assert.False((bool)successProp.GetValue(result));
        _mockClient.Verify(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()), Times.Never);
    }

    // Adversarial-review follow-up (minor): Guid.Parse(request.ConfirmationId) with no TryParse
    // guard throws an unhandled FormatException on a malformed (non-GUID) string, instead of the
    // same graceful "invalid confirmation" error the well-formed-but-unknown-id path above returns.
    // Mirrors HQ.Plugins.Email.SendEmail/DeleteEmail's identical gap; fixed here for this plugin.
    [Fact]
    public async Task ExecuteJavascript_WithMalformedConfirmationId_ReturnsErrorAndNeverRunsScript()
    {
        var mockNotification = new Mock<INotificationService>();
        LogDelegate logger = (level, msg, ex) => Task.CompletedTask;
        var service = new HeadlessBrowserService(_mockClient.Object, _config, logger, mockNotification.Object);

        var request = new ExecuteJavascriptArgs { Script = "return 1", ConfirmationId = "not-a-guid" };
        var result = await service.ExecuteJavascript(_config, request);

        var successProp = result.GetType().GetProperty("Success");
        Assert.False((bool)successProp.GetValue(result));
        _mockClient.Verify(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteJavascript_WithValidConfirmationId_RunsScript()
    {
        var confirmId = Guid.NewGuid();
        var mockNotification = new Mock<INotificationService>();
        Confirmation outConf = new Confirmation { Id = confirmId };
        mockNotification.Setup(n => n.DoesConfirmationExist(confirmId, out outConf)).Returns(true);
        LogDelegate logger = (level, msg, ex) => Task.CompletedTask;
        var service = new HeadlessBrowserService(_mockClient.Object, _config, logger, mockNotification.Object);

        var page = new Mock<IPage>();
        page.Setup(p => p.EvaluateAsync<object>(It.IsAny<string>(), It.IsAny<object>()))
            .ReturnsAsync((object)"ok");
        page.Setup(p => p.Url).Returns("https://example.com/");
        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .Returns<Func<IPage, Task<object>>>(f => f(page.Object));

        var request = new ExecuteJavascriptArgs { Script = "return 1", ConfirmationId = confirmId.ToString() };
        var result = await service.ExecuteJavascript(_config, request);

        var successProp = result.GetType().GetProperty("Success");
        Assert.True((bool)successProp.GetValue(result));
        _mockClient.Verify(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteJavascript_WithRequiresConfirmationFalse_RunsWithoutConfirmation()
    {
        var mockNotification = new Mock<INotificationService>();
        var noConfirmConfig = new ServiceConfig { Name = "HeadlessBrowser", RequiresConfirmation = false };
        LogDelegate logger = (level, msg, ex) => Task.CompletedTask;
        var service = new HeadlessBrowserService(_mockClient.Object, noConfirmConfig, logger, mockNotification.Object);

        var page = new Mock<IPage>();
        page.Setup(p => p.EvaluateAsync<object>(It.IsAny<string>(), It.IsAny<object>()))
            .ReturnsAsync((object)"ok");
        page.Setup(p => p.Url).Returns("https://example.com/");
        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .Returns<Func<IPage, Task<object>>>(f => f(page.Object));

        var result = await service.ExecuteJavascript(noConfirmConfig, new ExecuteJavascriptArgs { Script = "return 1" });

        mockNotification.Verify(n => n.RequestConfirmation(
            It.IsAny<string>(), It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()), Times.Never);
        var successProp = result.GetType().GetProperty("Success");
        Assert.True((bool)successProp.GetValue(result));
    }

    // WP6A-8 (Low): Path.Combine(dir, fileName) with an LLM-controlled fileName let an absolute
    // path discard `dir` entirely, or a "../.." fileName climb out of it. Path.GetFileName strips
    // both to a bare basename before the write path is built.
    [Theory]
    [InlineData("/ms-playwright/chromium-xxxx/chrome-linux/chrome", "chrome.png")]
    [InlineData("../../../app/plugin-data/HQ.Plugins.Email/cache.db", "cache.db.png")]
    [InlineData("../../etc/passwd", "passwd.png")]
    public async Task TakeScreenshot_WithPathTraversalOrAbsoluteFileName_WritesOnlyInsideConfiguredDirectory(
        string maliciousFileName, string expectedFileName)
    {
        var screenshotDir = Path.Combine(Path.GetTempPath(), "hb-screenshot-tests-" + Guid.NewGuid());
        _config.ScreenshotDirectory = screenshotDir;

        PageScreenshotOptions capturedOptions = null;
        var page = new Mock<IPage>();
        page.Setup(p => p.ScreenshotAsync(It.IsAny<PageScreenshotOptions>()))
            .Callback<PageScreenshotOptions>(opts => capturedOptions = opts)
            .ReturnsAsync(Array.Empty<byte>());
        _mockClient.Setup(c => c.ExecuteAsync(It.IsAny<Func<IPage, Task<object>>>()))
            .Returns<Func<IPage, Task<object>>>(f => f(page.Object));

        var result = await _service.TakeScreenshot(_config, new TakeScreenshotArgs { FileName = maliciousFileName });

        try
        {
            Assert.NotNull(capturedOptions);
            var resolvedDir = Path.GetFullPath(screenshotDir);
            var resolvedFile = Path.GetFullPath(capturedOptions.Path);
            Assert.StartsWith(resolvedDir + Path.DirectorySeparatorChar, resolvedFile);
            Assert.Equal(expectedFileName, Path.GetFileName(capturedOptions.Path));

            var json = JsonSerializer.Serialize(result);
            Assert.Contains("\"Success\":true", json);
        }
        finally
        {
            if (Directory.Exists(screenshotDir))
                Directory.Delete(screenshotDir, recursive: true);
        }
    }
}
