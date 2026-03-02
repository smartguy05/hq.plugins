using HQ.Models;
using HQ.Models.Extensions;
using HQ.Models.Interfaces;
using HQ.Plugins.Email;
using HQ.Plugins.Email.Models;
using Moq;

namespace HQ.Plugins.Tests.Email;

public class EmailServiceTests
{
    private readonly Mock<INotificationService> _mockNotification;
    private readonly EmailService _service;
    private readonly ServiceConfig _config;

    public EmailServiceTests()
    {
        _mockNotification = new Mock<INotificationService>();
        _service = new EmailService(_mockNotification.Object);
        _config = new ServiceConfig
        {
            Name = "test",
            Description = "Test email config",
            EmailAccounts = new List<EmailParameters>
            {
                new()
                {
                    Name = "personal",
                    DisplayName = "Test User",
                    Email = "test@gmail.com",
                    Default = true,
                    Imap = "imap.gmail.com",
                    ImapPort = 993,
                    Smtp = "smtp.gmail.com",
                    SmtpPort = 587,
                    Username = "test@gmail.com",
                    Password = "test-app-password",
                    UseSsl = true
                },
                new()
                {
                    Name = "work",
                    DisplayName = "Work User",
                    Email = "work@gmail.com",
                    Default = false,
                    Imap = "imap.gmail.com",
                    ImapPort = 993,
                    Smtp = "smtp.gmail.com",
                    SmtpPort = 587,
                    Username = "work@gmail.com",
                    Password = "work-app-password",
                    UseSsl = true
                }
            }
        };
    }

    #region Constructor and Service Discovery

    [Fact]
    public void Constructor_AcceptsNullNotificationService()
    {
        // Arrange & Act
        var service = new EmailService();

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_AcceptsNotificationService()
    {
        // Arrange & Act
        var service = new EmailService(_mockNotification.Object);

        // Assert
        Assert.NotNull(service);
    }

    [Fact]
    public void GetServiceFunctions_ReturnsAll17Tools()
    {
        // Arrange & Act
        var tools = _service.GetServiceFunctions();

        // Assert
        Assert.Equal(17, tools.Count);
    }

    [Fact]
    public void GetServiceFunctions_ContainsExpectedToolNames()
    {
        // Arrange
        var expectedNames = new[]
        {
            "get_email", "get_drafts", "get_email_summary", "send_email",
            "delete_email", "move_to_folder", "mark_as_read", "get_labels",
            "add_label", "remove_label", "archive", "star",
            "create_draft", "delete_draft", "get_attachments",
            "add_attachment_to_draft", "remove_attachment_from_draft"
        };

        // Act
        var tools = _service.GetServiceFunctions();
        var toolNames = tools.Select(t => t.FunctionName).ToList();

        // Assert
        foreach (var name in expectedNames)
        {
            Assert.Contains(name, toolNames);
        }
    }

    #endregion

    #region ProcessRequest Routing

    [Fact]
    public async Task ProcessRequest_RoutesToGetEmail()
    {
        // Arrange
        var request = new ServiceRequest { Method = "get_email", MessageId = "test-id-123" };

        // Act & Assert - should not throw "not supported", should attempt IMAP connection
        // (will fail with auth/connection error since no real server, but that proves routing works)
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.ProcessRequest(request, _config, _mockNotification.Object));
        // The error should be about IMAP connection, NOT "not supported"
        Assert.DoesNotContain("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequest_ReturnsNotSupported_ForInvalidMethod()
    {
        // Arrange
        var request = new ServiceRequest { Method = "invalid_method" };

        // Act
        var result = await _service.ProcessRequest(request, _config, _mockNotification.Object);

        // Assert
        var msgProp = result.GetType().GetProperty("Message");
        Assert.NotNull(msgProp);
        Assert.Contains("not supported", (string)msgProp.GetValue(result));
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task GetEmail_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest { MessageId = null };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.GetEmail(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task DeleteEmail_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest { MessageId = null };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.DeleteEmail(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task SendEmail_ThrowsWhenNoToOrMessageId()
    {
        // Arrange
        var request = new ServiceRequest { To = null, MessageId = null };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.SendEmail(_config, request));
        Assert.Contains("Must supply", ex.Message);
    }

    [Fact]
    public async Task MoveToFolder_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest { Folder = "Archive" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.MoveToFolder(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task MoveToFolder_ThrowsWhenFolderMissing()
    {
        // Arrange
        var request = new ServiceRequest { MessageId = "test-id", Folder = null };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.MoveToFolder(_config, request));
        Assert.Contains("Folder is required", ex.Message);
    }

    [Fact]
    public async Task MarkAsRead_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.MarkAsRead(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task AddLabel_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest { Label = "Important" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.AddLabel(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task AddLabel_ThrowsWhenLabelMissing()
    {
        // Arrange
        var request = new ServiceRequest { MessageId = "test-id" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.AddLabel(_config, request));
        Assert.Contains("Label is required", ex.Message);
    }

    [Fact]
    public async Task RemoveLabel_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest { Label = "Important" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.RemoveLabel(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task Archive_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.Archive(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task Star_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.Star(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task DeleteDraft_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.DeleteDraft(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task GetAttachments_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.GetAttachments(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task AddAttachmentToDraft_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.AddAttachmentToDraft(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task RemoveAttachmentFromDraft_ThrowsWhenMessageIdMissing()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.RemoveAttachmentFromDraft(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task GetDrafts_DoesNotRequireMessageId()
    {
        // Arrange - GetDrafts should NOT require MessageId (it lists all drafts)
        var request = new ServiceRequest { Account = "personal" };

        // Act & Assert - should attempt IMAP, not throw validation error
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetDrafts(_config, request));
        // Should fail on IMAP connection, NOT on "MessageId is required"
        Assert.DoesNotContain("MessageId is required", ex.Message);
    }

    #endregion

    #region Account Resolution

    [Fact]
    public async Task GetEmail_UsesDefaultAccountWhenNoneSpecified()
    {
        // Arrange
        var request = new ServiceRequest { MessageId = "test-id" };

        // Act & Assert - will fail on IMAP but proves account resolution works
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetEmail(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    [Fact]
    public async Task GetEmail_UsesNamedAccount()
    {
        // Arrange
        var request = new ServiceRequest { MessageId = "test-id", Account = "work" };

        // Act & Assert
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetEmail(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    [Fact]
    public async Task GetEmail_ThrowsWhenAccountNotFound()
    {
        // Arrange
        var configNoDefault = new ServiceConfig
        {
            Name = "test",
            EmailAccounts = new List<EmailParameters>
            {
                new() { Name = "work", Default = false }
            }
        };
        var request = new ServiceRequest { MessageId = "test-id", Account = "nonexistent" };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.GetEmail(configNoDefault, request));
        Assert.Contains("No mail accounts found", ex.Message);
    }

    #endregion

    #region SendEmail Confirmation Flow

    [Fact]
    public async Task SendEmail_RequestsConfirmation_WhenNoConfirmationId()
    {
        // Arrange
        var request = new ServiceRequest
        {
            To = "recipient@example.com",
            Subject = "Test",
            Body = "<p>Hello</p>"
        };
        _mockNotification
            .Setup(n => n.RequestConfirmation(
                It.IsAny<string>(),
                It.IsAny<Confirmation>(),
                It.IsAny<IPluginServiceRequest>()))
            .ReturnsAsync(new { Success = true, AwaitingConfirmation = true });

        // Act
        var result = await _service.SendEmail(_config, request);

        // Assert
        _mockNotification.Verify(n => n.RequestConfirmation(
            "HQ.Plugins.Email",
            It.Is<Confirmation>(c => c.ConfirmationMessage.Contains("send this email")),
            request), Times.Once);
    }

    [Fact]
    public async Task SendEmail_ReturnsError_WhenConfirmationIdInvalid()
    {
        // Arrange
        var confirmId = Guid.NewGuid();
        var request = new ServiceRequest
        {
            To = "recipient@example.com",
            ConfirmationId = confirmId.ToString()
        };
        Confirmation outConf = null;
        _mockNotification
            .Setup(n => n.DoesConfirmationExist(confirmId, out outConf))
            .Returns(false);

        // Act
        var result = await _service.SendEmail(_config, request);

        // Assert
        var successProp = result.GetType().GetProperty("Success");
        Assert.False((bool)successProp.GetValue(result));
    }

    #endregion

    #region DeleteEmail Confirmation Flow

    [Fact]
    public async Task DeleteEmail_RequestsConfirmation_WhenNoConfirmationId()
    {
        // Arrange
        var request = new ServiceRequest { MessageId = "test-id" };
        _mockNotification
            .Setup(n => n.RequestConfirmation(
                It.IsAny<string>(),
                It.IsAny<Confirmation>(),
                It.IsAny<IPluginServiceRequest>()))
            .ReturnsAsync(new { Success = true, AwaitingConfirmation = true });

        // Act
        var result = await _service.DeleteEmail(_config, request);

        // Assert
        _mockNotification.Verify(n => n.RequestConfirmation(
            "HQ.Plugins.Email",
            It.Is<Confirmation>(c => c.ConfirmationMessage.Contains("delete this email")),
            request), Times.Once);
    }

    [Fact]
    public async Task DeleteEmail_ReturnsError_WhenConfirmationIdInvalid()
    {
        // Arrange
        var confirmId = Guid.NewGuid();
        var request = new ServiceRequest
        {
            MessageId = "test-id",
            ConfirmationId = confirmId.ToString()
        };
        Confirmation outConf = null;
        _mockNotification
            .Setup(n => n.DoesConfirmationExist(confirmId, out outConf))
            .Returns(false);

        // Act
        var result = await _service.DeleteEmail(_config, request);

        // Assert
        var successProp = result.GetType().GetProperty("Success");
        Assert.False((bool)successProp.GetValue(result));
    }

    #endregion

    #region GetEmailSummary

    [Fact]
    public async Task GetEmailSummary_UsesDefaultAccount()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert - will fail on IMAP connection, not validation
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetEmailSummary(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    #endregion

    #region GetLabels

    [Fact]
    public async Task GetLabels_UsesDefaultAccount()
    {
        // Arrange
        var request = new ServiceRequest();

        // Act & Assert
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetLabels(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    #endregion

    #region CreateDraft

    [Fact]
    public async Task CreateDraft_UsesDefaultAccount()
    {
        // Arrange
        var request = new ServiceRequest
        {
            To = "test@example.com",
            Subject = "Test Draft",
            Body = "<p>Draft body</p>"
        };

        // Act & Assert - will fail on IMAP connection
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.CreateDraft(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    #endregion
}
