using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.ReportGenerator;
using HQ.Plugins.ReportGenerator.Models;
using Moq;

namespace HQ.Plugins.Tests.ReportGenerator;

public class ReportGeneratorFileStorageTests
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
    public async Task GenerateReport_WithProvider_WritesViaProvider()
    {
        // Arrange
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync("/workspace/reports/test.html");
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains(".report-index.json"))))
            .ReturnsAsync((string)null);

        var command = CreateCommandWithProvider(mockProvider);
        var config = new ServiceConfig { Name = "Test" };
        var request = new ServiceRequest
        {
            Method = "generate_report",
            Title = "Test Report",
            Content = "# Hello\n\nTest content",
            Format = "html"
        };

        // Act
        var result = await command.GenerateReport(config, request);

        // Assert — file written via provider, not direct File.WriteAllTextAsync
        mockProvider.Verify(p => p.WriteFileAsync(
            It.Is<string>(s => s.Contains(".html")),
            It.IsAny<string>(),
            false), Times.Once);
    }

    [Fact]
    public async Task GenerateReport_WithProvider_SavesReportIndex()
    {
        // Arrange
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync((string path, string content, bool b) => path);
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains(".report-index.json"))))
            .ReturnsAsync((string)null);

        var command = CreateCommandWithProvider(mockProvider);
        var config = new ServiceConfig { Name = "Test" };
        var request = new ServiceRequest
        {
            Method = "generate_report",
            Title = "Test Report",
            Content = "Test content",
            Format = "markdown"
        };

        // Act
        await command.GenerateReport(config, request);

        // Assert — report index written via provider
        mockProvider.Verify(p => p.WriteFileAsync(
            It.Is<string>(s => s.Contains(".report-index.json")),
            It.IsAny<string>(),
            false), Times.Once);
    }

    [Fact]
    public async Task ListReports_WithProvider_ReadsIndexViaProvider()
    {
        // Arrange
        var index = new Dictionary<string, object>
        {
            ["abc123"] = new
            {
                Id = "abc123",
                Title = "Test",
                FileName = "test.html",
                Format = "html",
                CreatedAt = "2026-01-01T00:00:00Z",
                FilePath = "/workspace/reports/test.html"
            }
        };
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains(".report-index.json"))))
            .ReturnsAsync(JsonSerializer.Serialize(index));
        mockProvider.Setup(p => p.FileExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var command = CreateCommandWithProvider(mockProvider);
        var config = new ServiceConfig { Name = "Test" };
        var request = new ServiceRequest { Method = "list_reports" };

        // Act
        var result = await command.ListReports(config, request);

        // Assert
        mockProvider.Verify(p => p.ReadFileAsync(It.Is<string>(s => s.Contains(".report-index.json"))), Times.Once);
    }

    [Fact]
    public async Task GetReport_WithProvider_ReadsContentViaProvider()
    {
        // Arrange
        var index = new Dictionary<string, object>
        {
            ["abc123"] = new
            {
                Id = "abc123",
                Title = "Test",
                FileName = "test.html",
                Format = "html",
                CreatedAt = "2026-01-01T00:00:00Z",
                FilePath = "/workspace/reports/test.html"
            }
        };
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains(".report-index.json"))))
            .ReturnsAsync(JsonSerializer.Serialize(index));
        mockProvider.Setup(p => p.FileExistsAsync("/workspace/reports/test.html"))
            .ReturnsAsync(true);
        mockProvider.Setup(p => p.ReadFileAsync("/workspace/reports/test.html"))
            .ReturnsAsync("<html>report content</html>");

        var command = CreateCommandWithProvider(mockProvider);
        var config = new ServiceConfig { Name = "Test" };
        var request = new ServiceRequest { Method = "get_report", ReportId = "abc123" };

        // Act
        var result = await command.GetReport(config, request);

        // Assert
        mockProvider.Verify(p => p.ReadFileAsync("/workspace/reports/test.html"), Times.Once);
    }

    [Fact]
    public async Task GenerateReport_WithoutProvider_FallsBackToLocalFileSystem()
    {
        // Arrange — no provider set, should use local file system
        var command = new ReportGeneratorCommand();
        command.Logger = TestLogger;
        var tempDir = Path.Combine(Path.GetTempPath(), $"hq-test-{Guid.NewGuid():N}");
        var config = new ServiceConfig { Name = "Test", OutputDirectory = tempDir };
        var request = new ServiceRequest
        {
            Method = "generate_report",
            Title = "Fallback Test",
            Content = "Fallback content",
            Format = "markdown"
        };

        try
        {
            // Act
            var result = await command.GenerateReport(config, request);

            // Assert — file exists on local filesystem
            var resultJson = JsonSerializer.Serialize(result);
            Assert.Contains("Success", resultJson);
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
