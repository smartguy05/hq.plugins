using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HQ.Models.Helpers;

namespace HQ.Plugins.Twilio.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. Fields used by a tool body but NOT advertised to the model are marked <c>[Injected]</c>
/// (kept out of the schema, still bindable).
/// </summary>

/// <summary>Args for tools that take no LLM parameters.</summary>
public class EmptyArgs;

// ── Messaging ──────────────────────────────────────────────

public class SendSmsArgs
{
    [Required, Description("Destination phone number in E.164 format (e.g. +15551234567)")]
    public string To { get; set; }

    [Required, Description("The text content of the message")]
    public string Body { get; set; }

    [Description("Twilio phone number to send from. Uses default if omitted.")]
    public string From { get; set; }

    [Description("URL of media to attach (MMS). Supports images, video, audio.")]
    public string MediaUrl { get; set; }
}

public class SendWhatsAppArgs
{
    [Required, Description("Destination phone number in E.164 format (e.g. +15551234567). The whatsapp: prefix is added automatically.")]
    public string To { get; set; }

    [Required, Description("The text content of the message")]
    public string Body { get; set; }

    [Description("Twilio WhatsApp-enabled number. Uses default if omitted.")]
    public string From { get; set; }

    [Description("URL of media to attach.")]
    public string MediaUrl { get; set; }
}

public class GetMessageArgs
{
    [Required, Description("The SID of the message to retrieve (starts with SM)")]
    public string MessageSid { get; set; }
}

public class ListMessagesArgs
{
    [Description("Number of messages to return. Defaults to 20.")]
    public int PageSize { get; set; } = 20;
}

// ── Voice ──────────────────────────────────────────────────

public class MakeCallArgs
{
    [Required, Description("Destination phone number in E.164 format")]
    public string To { get; set; }

    [Required, Description("TwiML instructions for the call, e.g. '<Response><Say voice=\"alice\">Hello from HQ</Say></Response>'")]
    public string Twiml { get; set; }

    [Description("Twilio phone number to call from. Uses default if omitted.")]
    public string From { get; set; }

    [Description("Whether to record the call. Defaults to false.")]
    public bool Record { get; set; }
}

public class GetCallArgs
{
    [Required, Description("The SID of the call to retrieve (starts with CA)")]
    public string CallSid { get; set; }
}

// ── Recordings ─────────────────────────────────────────────

public class ListRecordingsArgs
{
    [Description("Number of recordings to return. Defaults to 20.")]
    public int PageSize { get; set; } = 20;
}

public class GetRecordingArgs
{
    [Required, Description("The SID of the recording (starts with RE)")]
    public string RecordingSid { get; set; }
}

public class DeleteRecordingArgs
{
    [Required, Description("The SID of the recording to delete (starts with RE)")]
    public string RecordingSid { get; set; }
}

// ── Lookup ─────────────────────────────────────────────────

public class LookupPhoneNumberArgs
{
    [Required, Description("Phone number to look up in E.164 format (e.g. +15551234567)")]
    public string PhoneNumber { get; set; }
}

// ── Verify ─────────────────────────────────────────────────

public class SendVerificationArgs
{
    [Required, Description("Destination phone number (E.164) or email address")]
    public string To { get; set; }

    [Description("Delivery channel: sms, call, email, or whatsapp. Defaults to sms.")]
    public string Channel { get; set; } = "sms";
}

public class CheckVerificationArgs
{
    [Required, Description("The phone number or email that received the verification")]
    public string To { get; set; }

    [Required, Description("The verification code entered by the user")]
    public string Code { get; set; }
}

// ── Conversations ──────────────────────────────────────────

public class CreateConversationArgs
{
    [Description("Optional friendly name for the conversation")]
    public string FriendlyName { get; set; }
}

public class AddConversationParticipantArgs
{
    [Required, Description("The SID of the conversation (starts with CH)")]
    public string ConversationSid { get; set; }

    [Required, Description("Phone number of the participant in E.164 format")]
    public string ParticipantAddress { get; set; }

    [Description("Twilio number to proxy messages through. Uses default if omitted.")]
    public string From { get; set; }
}

public class SendConversationMessageArgs
{
    [Required, Description("The SID of the conversation (starts with CH)")]
    public string ConversationSid { get; set; }

    [Required, Description("The message text to send")]
    public string Body { get; set; }
}

public class ListConversationMessagesArgs
{
    [Required, Description("The SID of the conversation (starts with CH)")]
    public string ConversationSid { get; set; }

    [Description("Number of messages to return. Defaults to 20.")]
    public int PageSize { get; set; } = 20;
}
