using HQ.Models.Interfaces;

namespace HQ.Plugins.Twilio.Models;

public class ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    // Messaging
    public string To { get; set; }
    public string From { get; set; }
    public string Body { get; set; }
    public string MediaUrl { get; set; }
    public string MessageSid { get; set; }

    // Voice
    public string Twiml { get; set; }
    public string CallSid { get; set; }
    public bool Record { get; set; }

    // Lookup
    public string PhoneNumber { get; set; }

    // Verify
    public string Channel { get; set; } = "sms";
    public string Code { get; set; }

    // Conversations
    public string ConversationSid { get; set; }
    public string FriendlyName { get; set; }
    public string ParticipantAddress { get; set; }

    // Recordings
    public string RecordingSid { get; set; }

    // Pagination
    public int PageSize { get; set; } = 20;
}
