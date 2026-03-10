using HQ.Plugins.Email;
using HQ.Plugins.Email.Models;

namespace HQ.Plugins.Tests.Email;

/// <summary>
/// Integration tests for EmailService that require real Gmail credentials.
/// Configure a Gmail App Password and update the config below to run.
/// Excluded from CI/CD via [Trait("Category", "Integration")].
/// </summary>
public class EmailServiceIntegrationTests
{
    private readonly EmailService _service;
    private readonly ServiceConfig _config;

    public EmailServiceIntegrationTests()
    {
        _service = new EmailService();
        _config = new ServiceConfig
        {
            Name = "Gmail Integration",
            Description = "Integration test config",
            EmailAccounts = new List<EmailParameters>
            {
                new()
                {
                    Name = "test",
                    DisplayName = "Integration Test",
                    Email = "your-email@gmail.com",
                    Default = true,
                    Imap = "imap.gmail.com",
                    ImapPort = 993,
                    Smtp = "smtp.gmail.com",
                    SmtpPort = 587,
                    Username = "your-email@gmail.com",
                    Password = "your-16-char-app-password",
                    UseSsl = true
                }
            }
        };
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Dependency", "Gmail")]
    public async Task GetEmailSummary_ReturnsEmails()
    {
        // Arrange
        var request = new ServiceRequest { MaxReturnedEmails = 3 };

        // Act
        var result = await _service.GetEmailSummary(_config, request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Dependency", "Gmail")]
    public async Task GetFolders_ReturnsFolders()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act
        var result = await _service.GetFolders(_config, request);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Dependency", "Gmail")]
    public async Task GetDrafts_ReturnsDrafts()
    {
        // Arrange
        var request = new ServiceRequest { MaxReturnedEmails = 5 };

        // Act
        var result = await _service.GetDrafts(_config, request);

        // Assert
        Assert.NotNull(result);
    }
}
