using HQ.Models.Interfaces;

namespace HQ.Plugins.Email.Models;

public record ServiceRequest: IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
    public string Account { get; set; }
    public string RecipientName { get; set; }
    public string To { get; set; }
    public string Subject { get; set; }
    public string Body { get; set; }
    public string Sender { get; set; }
    public string SearchSubject { get; set; }
    public string MessageId { get; set; }
    public int MaxReturnedEmails { get; set; } = 10;
    public bool UnreadOnly { get; set; }
    public string EmailsSentAfter { get; set; }
    public string EmailsSentBefore { get; set; }
    public string Label { get; set; }
    public string Folder { get; set; }
    public bool? MarkAsRead { get; set; }
    public bool? Flag { get; set; }
    public object Attachment { get; set; }

    // Semantic/local search
    public string Query { get; set; }
    public string SearchText { get; set; }
    public int? MaxResults { get; set; }
}