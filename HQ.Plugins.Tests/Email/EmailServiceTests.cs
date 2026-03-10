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
        var service = new EmailService();
        Assert.NotNull(service);
    }

    [Fact]
    public void Constructor_AcceptsNotificationService()
    {
        var service = new EmailService(_mockNotification.Object);
        Assert.NotNull(service);
    }

    [Fact]
    public void GetServiceFunctions_ReturnsAll17Tools()
    {
        var tools = _service.GetServiceFunctions();
        Assert.Equal(17, tools.Count);
    }

    [Fact]
    public void GetServiceFunctions_ContainsExpectedToolNames()
    {
        var expectedNames = new[]
        {
            "get_email", "get_drafts", "get_email_summary", "send_email",
            "delete_email", "move_to_folder", "mark_as_read", "flag_email",
            "create_draft", "delete_draft", "get_attachments",
            "add_attachment_to_draft", "remove_attachment_from_draft",
            "search_emails", "search_emails_local", "sync_emails", "get_folders"
        };

        var tools = _service.GetServiceFunctions();
        var toolNames = tools.Select(t => t.FunctionName).ToList();

        foreach (var name in expectedNames)
        {
            Assert.Contains(name, toolNames);
        }
    }

    [Fact]
    public void GetServiceFunctions_DoesNotContainRemovedTools()
    {
        var removedNames = new[] { "archive", "add_label", "remove_label", "get_labels", "star" };

        var tools = _service.GetServiceFunctions();
        var toolNames = tools.Select(t => t.FunctionName).ToList();

        foreach (var name in removedNames)
        {
            Assert.DoesNotContain(name, toolNames);
        }
    }

    #endregion

    #region ProcessRequest Routing

    [Fact]
    public async Task ProcessRequest_RoutesToGetEmail()
    {
        var request = new ServiceRequest { Method = "get_email", MessageId = "test-id-123" };

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.ProcessRequest(request, _config, _mockNotification.Object));
        Assert.DoesNotContain("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRequest_ReturnsNotSupported_ForInvalidMethod()
    {
        var request = new ServiceRequest { Method = "invalid_method" };

        var result = await _service.ProcessRequest(request, _config, _mockNotification.Object);

        var msgProp = result.GetType().GetProperty("Message");
        Assert.NotNull(msgProp);
        Assert.Contains("not supported", (string)msgProp.GetValue(result));
    }

    [Fact]
    public async Task ProcessRequest_ReturnsNotSupported_ForRemovedMethods()
    {
        foreach (var method in new[] { "archive", "add_label", "remove_label", "get_labels", "star" })
        {
            var request = new ServiceRequest { Method = method };
            var result = await _service.ProcessRequest(request, _config, _mockNotification.Object);

            var msgProp = result.GetType().GetProperty("Message");
            Assert.NotNull(msgProp);
            Assert.Contains("not supported", (string)msgProp.GetValue(result));
        }
    }

    #endregion

    #region Validation Tests

    [Fact]
    public async Task GetEmail_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest { MessageId = null };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.GetEmail(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task DeleteEmail_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest { MessageId = null };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.DeleteEmail(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task SendEmail_ThrowsWhenNoToOrMessageId()
    {
        var request = new ServiceRequest { To = null, MessageId = null };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.SendEmail(_config, request));
        Assert.Contains("Must supply", ex.Message);
    }

    [Fact]
    public async Task MoveToFolder_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest { Folder = "Archive" };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.MoveToFolder(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task MoveToFolder_ThrowsWhenFolderMissing()
    {
        var request = new ServiceRequest { MessageId = "test-id", Folder = null };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.MoveToFolder(_config, request));
        Assert.Contains("Folder is required", ex.Message);
    }

    [Fact]
    public async Task MarkAsRead_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.MarkAsRead(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task FlagEmail_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.FlagEmail(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task DeleteDraft_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.DeleteDraft(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task GetAttachments_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.GetAttachments(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task AddAttachmentToDraft_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.AddAttachmentToDraft(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task RemoveAttachmentFromDraft_ThrowsWhenMessageIdMissing()
    {
        var request = new ServiceRequest();

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.RemoveAttachmentFromDraft(_config, request));
        Assert.Contains("MessageId is required", ex.Message);
    }

    [Fact]
    public async Task GetDrafts_DoesNotRequireMessageId()
    {
        var request = new ServiceRequest { Account = "personal" };

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetDrafts(_config, request));
        Assert.DoesNotContain("MessageId is required", ex.Message);
    }

    #endregion

    #region Account Resolution

    [Fact]
    public async Task GetEmail_UsesDefaultAccountWhenNoneSpecified()
    {
        var request = new ServiceRequest { MessageId = "test-id" };

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetEmail(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    [Fact]
    public async Task GetEmail_UsesNamedAccount()
    {
        var request = new ServiceRequest { MessageId = "test-id", Account = "work" };

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetEmail(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    [Fact]
    public async Task GetEmail_ThrowsWhenAccountNotFound()
    {
        var configNoDefault = new ServiceConfig
        {
            Name = "test",
            EmailAccounts = new List<EmailParameters>
            {
                new() { Name = "work", Default = false }
            }
        };
        var request = new ServiceRequest { MessageId = "test-id", Account = "nonexistent" };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.GetEmail(configNoDefault, request));
        Assert.Contains("No mail accounts found", ex.Message);
    }

    #endregion

    #region SendEmail Confirmation Flow

    [Fact]
    public async Task SendEmail_RequestsConfirmation_WhenNoConfirmationId()
    {
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

        var result = await _service.SendEmail(_config, request);

        _mockNotification.Verify(n => n.RequestConfirmation(
            "HQ.Plugins.Email",
            It.Is<Confirmation>(c => c.ConfirmationMessage.Contains("send this email")),
            request), Times.Once);
    }

    [Fact]
    public async Task SendEmail_ReturnsError_WhenConfirmationIdInvalid()
    {
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

        var result = await _service.SendEmail(_config, request);

        var successProp = result.GetType().GetProperty("Success");
        Assert.False((bool)successProp.GetValue(result));
    }

    #endregion

    #region DeleteEmail Confirmation Flow

    [Fact]
    public async Task DeleteEmail_RequestsConfirmation_WhenNoConfirmationId()
    {
        var request = new ServiceRequest { MessageId = "test-id" };
        _mockNotification
            .Setup(n => n.RequestConfirmation(
                It.IsAny<string>(),
                It.IsAny<Confirmation>(),
                It.IsAny<IPluginServiceRequest>()))
            .ReturnsAsync(new { Success = true, AwaitingConfirmation = true });

        var result = await _service.DeleteEmail(_config, request);

        _mockNotification.Verify(n => n.RequestConfirmation(
            "HQ.Plugins.Email",
            It.Is<Confirmation>(c => c.ConfirmationMessage.Contains("delete this email")),
            request), Times.Once);
    }

    [Fact]
    public async Task DeleteEmail_ReturnsError_WhenConfirmationIdInvalid()
    {
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

        var result = await _service.DeleteEmail(_config, request);

        var successProp = result.GetType().GetProperty("Success");
        Assert.False((bool)successProp.GetValue(result));
    }

    #endregion

    #region RequiresConfirmation = false

    [Fact]
    public async Task SendEmail_SkipsConfirmation_WhenRequiresConfirmationFalse()
    {
        var noConfirmConfig = new ServiceConfig
        {
            Name = "test",
            Description = "Test",
            RequiresConfirmation = false,
            EmailAccounts = _config.EmailAccounts
        };
        var request = new ServiceRequest
        {
            To = "recipient@example.com",
            Subject = "Test",
            Body = "<p>Hello</p>"
        };

        // Should skip confirmation and go straight to IMAP (which will fail on connection)
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.SendEmail(noConfirmConfig, request));
        // Should NOT have called RequestConfirmation
        _mockNotification.Verify(n => n.RequestConfirmation(
            It.IsAny<string>(),
            It.IsAny<Confirmation>(),
            It.IsAny<IPluginServiceRequest>()), Times.Never);
    }

    [Fact]
    public async Task DeleteEmail_SkipsConfirmation_WhenRequiresConfirmationFalse()
    {
        var noConfirmConfig = new ServiceConfig
        {
            Name = "test",
            Description = "Test",
            RequiresConfirmation = false,
            EmailAccounts = _config.EmailAccounts
        };
        var request = new ServiceRequest { MessageId = "test-id" };

        // Should skip confirmation and go straight to IMAP (which will fail on connection)
        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.DeleteEmail(noConfirmConfig, request));
        // Should NOT have called RequestConfirmation
        _mockNotification.Verify(n => n.RequestConfirmation(
            It.IsAny<string>(),
            It.IsAny<Confirmation>(),
            It.IsAny<IPluginServiceRequest>()), Times.Never);
    }

    #endregion

    #region GetEmailSummary

    [Fact]
    public async Task GetEmailSummary_UsesDefaultAccount()
    {
        var request = new ServiceRequest();

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.GetEmailSummary(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    #endregion

    #region New Tools - No Store Configured

    [Fact]
    public async Task SearchEmails_ReturnsNotConfigured_WhenNoVectorService()
    {
        var request = new ServiceRequest { Query = "test query" };
        var result = await _service.SearchEmails(_config, request);

        var msgProp = result.GetType().GetProperty("Message");
        Assert.NotNull(msgProp);
        Assert.Contains("not configured", (string)msgProp.GetValue(result));
    }

    [Fact]
    public async Task SearchEmails_ThrowsWhenQueryMissing()
    {
        var request = new ServiceRequest { Query = null };

        var ex = await Assert.ThrowsAsync<Exception>(
            () => _service.SearchEmails(_config, request));
        Assert.Contains("Query is required", ex.Message);
    }

    [Fact]
    public async Task SearchEmailsLocal_ReturnsNotConfigured_WhenNoStore()
    {
        var request = new ServiceRequest { SearchText = "test" };
        var result = await _service.SearchEmailsLocal(_config, request);

        var msgProp = result.GetType().GetProperty("Message");
        Assert.NotNull(msgProp);
        Assert.Contains("not configured", (string)msgProp.GetValue(result));
    }

    [Fact]
    public async Task SyncEmails_ReturnsNotConfigured_WhenNoSyncEngine()
    {
        var request = new ServiceRequest();
        var result = await _service.SyncEmails(_config, request);

        var msgProp = result.GetType().GetProperty("Message");
        Assert.NotNull(msgProp);
        Assert.Contains("not configured", (string)msgProp.GetValue(result));
    }

    [Fact]
    public async Task GetFolders_ReturnsNotConfigured_WhenNoStore()
    {
        var request = new ServiceRequest();
        var result = await _service.GetFolders(_config, request);

        var msgProp = result.GetType().GetProperty("Message");
        Assert.NotNull(msgProp);
        Assert.Contains("not configured", (string)msgProp.GetValue(result));
    }

    #endregion

    #region CreateDraft

    [Fact]
    public async Task CreateDraft_UsesDefaultAccount()
    {
        var request = new ServiceRequest
        {
            To = "test@example.com",
            Subject = "Test Draft",
            Body = "<p>Draft body</p>"
        };

        var ex = await Assert.ThrowsAnyAsync<Exception>(
            () => _service.CreateDraft(_config, request));
        Assert.DoesNotContain("No mail accounts found", ex.Message);
    }

    #endregion
}
