using HQ.Plugins.Email.Models;

namespace HQ.Plugins.Tests.Email;

/// <summary>
/// Validates the per-tool args records (which replaced the old ServiceRequest property bag)
/// expose the expected properties and defaults. The fields formerly carried on ServiceRequest
/// now live on the tool-specific args records that own them.
/// </summary>
public class ServiceRequestTests
{
    [Fact]
    public void GetEmailSummaryArgs_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var request = new GetEmailSummaryArgs();

        // Assert
        Assert.Equal(10, request.MaxReturnedEmails);
        Assert.False(request.UnreadOnly);
        Assert.Null(request.Account);
        Assert.Null(request.To);
        Assert.Null(request.Subject);
        Assert.Null(request.Sender);
        Assert.Null(request.MessageId);
    }

    [Fact]
    public void SendEmailArgs_ShouldSetProperties()
    {
        // Arrange & Act
        var request = new SendEmailArgs
        {
            Method = "send_email",
            Account = "work",
            To = "test@example.com",
            Subject = "Test Subject",
            Body = "Test Body",
            RecipientName = "Test User",
            MessageId = "msg-123",
            ToolCallId = "tool-456",
            RequestingService = "TestService",
            ConfirmationId = "conf-789"
        };

        // Assert
        Assert.Equal("send_email", request.Method);
        Assert.Equal("work", request.Account);
        Assert.Equal("test@example.com", request.To);
        Assert.Equal("Test Subject", request.Subject);
        Assert.Equal("Test Body", request.Body);
        Assert.Equal("Test User", request.RecipientName);
        Assert.Equal("msg-123", request.MessageId);
        Assert.Equal("tool-456", request.ToolCallId);
        Assert.Equal("TestService", request.RequestingService);
        Assert.Equal("conf-789", request.ConfirmationId);
    }

    [Fact]
    public void GetEmailSummaryArgs_ShouldSetSearchProperties()
    {
        // Arrange & Act
        var request = new GetEmailSummaryArgs
        {
            MaxReturnedEmails = 5,
            UnreadOnly = true,
            Sender = "sender@example.com",
            SearchSubject = "Search",
            MessageId = "msg-123"
        };

        // Assert
        Assert.Equal(5, request.MaxReturnedEmails);
        Assert.True(request.UnreadOnly);
        Assert.Equal("sender@example.com", request.Sender);
        Assert.Equal("Search", request.SearchSubject);
        Assert.Equal("msg-123", request.MessageId);
    }

    [Fact]
    public void GetEmailSummaryArgs_DateFilters_ShouldWork()
    {
        // Arrange & Act
        var request = new GetEmailSummaryArgs
        {
            EmailsSentAfter = "2023-01-01",
            EmailsSentBefore = "2023-12-31"
        };

        // Assert
        Assert.Equal("2023-01-01", request.EmailsSentAfter);
        Assert.Equal("2023-12-31", request.EmailsSentBefore);
    }
}
