using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Interfaces;
using HQ.Plugins.JobBoard;
using HQ.Plugins.JobBoard.Models;
using Moq;

namespace HQ.Plugins.Tests.JobBoard;

public class JobBoardFileStorageTests
{
    private static readonly LogDelegate TestLogger = (level, message, exception) => Task.CompletedTask;

    [Fact]
    public void JobBoardCommand_AcceptsFileStorageProvider()
    {
        var command = new JobBoardCommand();
        var mockProvider = new Mock<IFileStorageProvider>();
        ((ICommand)command).SetFileStorageProvider(mockProvider.Object);
        // Should not throw
    }

    [Fact]
    public async Task JobBoardService_CacheJobListings_UsesProviderWhenAvailable()
    {
        // Arrange
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains("job-cache.json"))))
            .ReturnsAsync((string)null);
        mockProvider.Setup(p => p.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync((string path, string content, bool b) => path);

        var config = new ServiceConfig { Name = "Test" };
        var service = new JobBoardService(config, TestLogger, mockProvider.Object);

        var request = new ServiceRequest
        {
            Method = "get_job_details",
            JobId = "nonexistent"
        };

        // Act — trigger cache load
        var result = await service.GetJobDetails(config, request);

        // Assert — cache loaded via provider
        mockProvider.Verify(p => p.ReadFileAsync(It.Is<string>(s => s.Contains("job-cache.json"))), Times.Once);
    }

    [Fact]
    public async Task JobBoardService_LoadApplications_UsesProviderWhenAvailable()
    {
        // Arrange
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains("applications.json"))))
            .ReturnsAsync((string)null);

        var config = new ServiceConfig { Name = "Test" };
        var service = new JobBoardService(config, TestLogger, mockProvider.Object);

        var request = new ServiceRequest { Method = "get_applications" };

        // Act
        var result = await service.GetApplications(config, request);

        // Assert
        mockProvider.Verify(p => p.ReadFileAsync(It.Is<string>(s => s.Contains("applications.json"))), Times.Once);
    }

    [Fact]
    public async Task JobBoardService_TrackApplication_SavesViaProvider()
    {
        // Arrange
        var mockProvider = new Mock<IFileStorageProvider>();
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains("applications.json"))))
            .ReturnsAsync((string)null);
        mockProvider.Setup(p => p.ReadFileAsync(It.Is<string>(s => s.Contains("job-cache.json"))))
            .ReturnsAsync((string)null);
        mockProvider.Setup(p => p.WriteFileAsync(It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync((string path, string content, bool b) => path);

        var config = new ServiceConfig { Name = "Test" };
        var service = new JobBoardService(config, TestLogger, mockProvider.Object);

        var request = new ServiceRequest
        {
            Method = "track_application",
            JobId = "indeed-12345678",
            Notes = "Applied via email"
        };

        // Act
        var result = await service.TrackApplication(config, request);

        // Assert — applications saved via provider
        mockProvider.Verify(p => p.WriteFileAsync(
            It.Is<string>(s => s.Contains("applications.json")),
            It.IsAny<string>(),
            false), Times.Once);
    }

    [Fact]
    public async Task JobBoardService_WithoutProvider_FallsBackToLocalFileSystem()
    {
        // Arrange — no provider (null)
        var tempDir = Path.Combine(Path.GetTempPath(), $"hq-test-jb-{Guid.NewGuid():N}");
        var config = new ServiceConfig { Name = "Test", DataDirectory = tempDir };
        var service = new JobBoardService(config, TestLogger, null);

        var request = new ServiceRequest { Method = "get_applications" };

        try
        {
            // Act
            var result = await service.GetApplications(config, request);

            // Assert — didn't throw, fell back to local
            var resultJson = JsonSerializer.Serialize(result);
            Assert.Contains("Total", resultJson);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
