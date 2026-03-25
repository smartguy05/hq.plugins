using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.Twilio.Models;

namespace HQ.Plugins.Twilio;

public class TwilioCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Twilio";
    public override string Description => "A plugin for SMS, voice calls, phone lookup, verification, and conversations via Twilio";
    protected override INotificationService NotificationService { get; set; }

    // For unit testing: inject a custom HttpMessageHandler
    internal HttpMessageHandler HttpHandler { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(serviceRequest, config, NotificationService);
    }

    private TwilioClient CreateClient(ServiceConfig config)
    {
        return new TwilioClient(config.AccountSid, config.AuthToken, HttpHandler);
    }

    private string ResolveFrom(ServiceConfig config, ServiceRequest serviceRequest)
    {
        return !string.IsNullOrWhiteSpace(serviceRequest.From) ? serviceRequest.From : config.DefaultFromNumber;
    }

    private static object ToResult(JsonDocument doc)
    {
        return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
    }

    /// <summary>
    /// Checks for Twilio error responses. Twilio uses "error_code" for message-level errors
    /// and "code" for HTTP-level errors (auth failures, invalid requests, etc.).
    /// </summary>
    private static bool IsErrorResponse(JsonElement root, out string message)
    {
        // Check for message-level errors (error_code is non-null)
        if (root.TryGetProperty("error_code", out var errCode) && errCode.ValueKind != JsonValueKind.Null)
        {
            message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
            return true;
        }

        // Check for HTTP-level errors (have "code" but no "sid")
        if (root.TryGetProperty("code", out _) && !root.TryGetProperty("sid", out _))
        {
            message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
            return true;
        }

        // No sid means something unexpected happened
        if (!root.TryGetProperty("sid", out _))
        {
            message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Unexpected response from Twilio";
            return true;
        }

        message = null;
        return false;
    }

    // ── Messaging ──────────────────────────────────────────────

    [Display(Name = "send_sms")]
    [Description("Sends an SMS or MMS message to a phone number.")]
    [Parameters("""{"type":"object","properties":{"to":{"type":"string","description":"Destination phone number in E.164 format (e.g. +15551234567)"},"body":{"type":"string","description":"The text content of the message"},"from":{"type":"string","description":"Twilio phone number to send from. Uses default if omitted."},"mediaUrl":{"type":"string","description":"URL of media to attach (MMS). Supports images, video, audio."}},"required":["to","body"]}""")]
    public async Task<object> SendSms(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, serviceRequest);
        var doc = await client.SendSms(from, serviceRequest.To, serviceRequest.Body, serviceRequest.MediaUrl);

        if (IsErrorResponse(doc.RootElement, out var errorMessage))
        {
            await Log(LogLevel.Warning, $"SMS send failed: {errorMessage}");
            return new { Success = false, Message = errorMessage };
        }

        await Log(LogLevel.Info, $"SMS sent to {serviceRequest.To}");
        var sid = doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        var status = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        return new { Success = true, MessageSid = sid, Status = status };
    }

    [Display(Name = "send_whatsapp")]
    [Description("Sends a WhatsApp message to a phone number via Twilio.")]
    [Parameters("""{"type":"object","properties":{"to":{"type":"string","description":"Destination phone number in E.164 format (e.g. +15551234567). The whatsapp: prefix is added automatically."},"body":{"type":"string","description":"The text content of the message"},"from":{"type":"string","description":"Twilio WhatsApp-enabled number. Uses default if omitted."},"mediaUrl":{"type":"string","description":"URL of media to attach."}},"required":["to","body"]}""")]
    public async Task<object> SendWhatsApp(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, serviceRequest);
        var doc = await client.SendWhatsApp(from, serviceRequest.To, serviceRequest.Body, serviceRequest.MediaUrl);

        if (IsErrorResponse(doc.RootElement, out var errorMessage))
        {
            await Log(LogLevel.Warning, $"WhatsApp send failed: {errorMessage}");
            return new { Success = false, Message = errorMessage };
        }

        await Log(LogLevel.Info, $"WhatsApp message sent to {serviceRequest.To}");
        var sid = doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        var status = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        return new { Success = true, MessageSid = sid, Status = status };
    }

    [Display(Name = "get_message")]
    [Description("Gets details and delivery status of a specific SMS/MMS message by its SID.")]
    [Parameters("""{"type":"object","properties":{"messageSid":{"type":"string","description":"The SID of the message to retrieve (starts with SM)"}},"required":["messageSid"]}""")]
    public async Task<object> GetMessage(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.GetMessage(serviceRequest.MessageSid);
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "list_messages")]
    [Description("Lists recent SMS/MMS messages on the account.")]
    [Parameters("""{"type":"object","properties":{"pageSize":{"type":"integer","description":"Number of messages to return. Defaults to 20."}},"required":[]}""")]
    public async Task<object> ListMessages(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.ListMessages(serviceRequest.PageSize);
        return new { Success = true, Data = ToResult(doc) };
    }

    // ── Voice ──────────────────────────────────────────────────

    [Display(Name = "make_call")]
    [Description("Initiates an outbound phone call. Use TwiML to control what happens on the call (e.g. <Say> for text-to-speech, <Play> for audio, <Gather> for input).")]
    [Parameters("""{"type":"object","properties":{"to":{"type":"string","description":"Destination phone number in E.164 format"},"twiml":{"type":"string","description":"TwiML instructions for the call, e.g. '<Response><Say voice=\"alice\">Hello from HQ</Say></Response>'"},"from":{"type":"string","description":"Twilio phone number to call from. Uses default if omitted."},"record":{"type":"boolean","description":"Whether to record the call. Defaults to false."}},"required":["to","twiml"]}""")]
    public async Task<object> MakeCall(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, serviceRequest);
        var doc = await client.MakeCall(from, serviceRequest.To, serviceRequest.Twiml, serviceRequest.Record);

        if (IsErrorResponse(doc.RootElement, out var errorMessage))
        {
            await Log(LogLevel.Warning, $"Call failed: {errorMessage}");
            return new { Success = false, Message = errorMessage };
        }

        await Log(LogLevel.Info, $"Call initiated to {serviceRequest.To}");
        var sid = doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        var status = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        return new { Success = true, CallSid = sid, Status = status };
    }

    [Display(Name = "get_call")]
    [Description("Gets details and status of a specific phone call by its SID.")]
    [Parameters("""{"type":"object","properties":{"callSid":{"type":"string","description":"The SID of the call to retrieve (starts with CA)"}},"required":["callSid"]}""")]
    public async Task<object> GetCall(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.GetCall(serviceRequest.CallSid);
        return new { Success = true, Data = ToResult(doc) };
    }

    // ── Recordings ─────────────────────────────────────────────

    [Display(Name = "list_recordings")]
    [Description("Lists call recordings on the account.")]
    [Parameters("""{"type":"object","properties":{"pageSize":{"type":"integer","description":"Number of recordings to return. Defaults to 20."}},"required":[]}""")]
    public async Task<object> ListRecordings(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.ListRecordings(serviceRequest.PageSize);
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "get_recording")]
    [Description("Gets metadata for a specific call recording by its SID.")]
    [Parameters("""{"type":"object","properties":{"recordingSid":{"type":"string","description":"The SID of the recording (starts with RE)"}},"required":["recordingSid"]}""")]
    public async Task<object> GetRecording(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.GetRecording(serviceRequest.RecordingSid);
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "delete_recording")]
    [Description("Deletes a call recording by its SID.")]
    [Parameters("""{"type":"object","properties":{"recordingSid":{"type":"string","description":"The SID of the recording to delete (starts with RE)"}},"required":["recordingSid"]}""")]
    public async Task<object> DeleteRecording(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        await client.DeleteRecording(serviceRequest.RecordingSid);
        await Log(LogLevel.Info, $"Recording {serviceRequest.RecordingSid} deleted");
        return new { Success = true, Message = $"Recording {serviceRequest.RecordingSid} deleted" };
    }

    // ── Lookup ─────────────────────────────────────────────────

    [Display(Name = "lookup_phone_number")]
    [Description("Looks up information about a phone number including carrier, line type (mobile/landline/VoIP), and caller name.")]
    [Parameters("""{"type":"object","properties":{"phoneNumber":{"type":"string","description":"Phone number to look up in E.164 format (e.g. +15551234567)"}},"required":["phoneNumber"]}""")]
    public async Task<object> LookupPhoneNumber(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.LookupPhoneNumber(serviceRequest.PhoneNumber);
        await Log(LogLevel.Info, $"Looked up {serviceRequest.PhoneNumber}");
        return new { Success = true, Data = ToResult(doc) };
    }

    // ── Verify ─────────────────────────────────────────────────

    [Display(Name = "send_verification")]
    [Description("Sends a verification code (OTP) to a phone number or email via Twilio Verify.")]
    [Parameters("""{"type":"object","properties":{"to":{"type":"string","description":"Destination phone number (E.164) or email address"},"channel":{"type":"string","description":"Delivery channel: sms, call, email, or whatsapp. Defaults to sms."}},"required":["to"]}""")]
    public async Task<object> SendVerification(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(config.VerifyServiceSid))
        {
            return new { Success = false, Message = "Verify Service SID is not configured" };
        }

        using var client = CreateClient(config);
        var doc = await client.SendVerification(config.VerifyServiceSid, serviceRequest.To, serviceRequest.Channel);

        if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "pending")
        {
            await Log(LogLevel.Info, $"Verification sent to {serviceRequest.To} via {serviceRequest.Channel}");
            return new { Success = true, Status = "pending", Message = $"Verification code sent to {serviceRequest.To}" };
        }

        return new { Success = false, Data = ToResult(doc) };
    }

    [Display(Name = "check_verification")]
    [Description("Checks a verification code (OTP) submitted by a user against Twilio Verify.")]
    [Parameters("""{"type":"object","properties":{"to":{"type":"string","description":"The phone number or email that received the verification"},"code":{"type":"string","description":"The verification code entered by the user"}},"required":["to","code"]}""")]
    public async Task<object> CheckVerification(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(config.VerifyServiceSid))
        {
            return new { Success = false, Message = "Verify Service SID is not configured" };
        }

        using var client = CreateClient(config);
        var doc = await client.CheckVerification(config.VerifyServiceSid, serviceRequest.To, serviceRequest.Code);

        var verifyStatus = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "unknown";
        var approved = verifyStatus == "approved";

        await Log(LogLevel.Info, $"Verification check for {serviceRequest.To}: {verifyStatus}");
        return new { Success = approved, Status = verifyStatus, Valid = approved };
    }

    // ── Conversations ──────────────────────────────────────────

    [Display(Name = "create_conversation")]
    [Description("Creates a new Twilio Conversation for multi-party messaging across SMS, WhatsApp, and chat.")]
    [Parameters("""{"type":"object","properties":{"friendlyName":{"type":"string","description":"Optional friendly name for the conversation"}},"required":[]}""")]
    public async Task<object> CreateConversation(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.CreateConversation(serviceRequest.FriendlyName);

        if (IsErrorResponse(doc.RootElement, out var errorMessage))
        {
            await Log(LogLevel.Warning, $"Create conversation failed: {errorMessage}");
            return new { Success = false, Message = errorMessage };
        }

        var sid = doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        await Log(LogLevel.Info, $"Conversation created: {sid}");
        return new { Success = true, ConversationSid = sid, Data = ToResult(doc) };
    }

    [Display(Name = "add_conversation_participant")]
    [Description("Adds an SMS or WhatsApp participant to an existing Twilio Conversation.")]
    [Parameters("""{"type":"object","properties":{"conversationSid":{"type":"string","description":"The SID of the conversation (starts with CH)"},"participantAddress":{"type":"string","description":"Phone number of the participant in E.164 format"},"from":{"type":"string","description":"Twilio number to proxy messages through. Uses default if omitted."}},"required":["conversationSid","participantAddress"]}""")]
    public async Task<object> AddConversationParticipant(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, serviceRequest);
        var doc = await client.AddConversationParticipant(serviceRequest.ConversationSid, serviceRequest.ParticipantAddress, from);
        await Log(LogLevel.Info, $"Participant {serviceRequest.ParticipantAddress} added to {serviceRequest.ConversationSid}");
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "send_conversation_message")]
    [Description("Sends a message to all participants in a Twilio Conversation.")]
    [Parameters("""{"type":"object","properties":{"conversationSid":{"type":"string","description":"The SID of the conversation (starts with CH)"},"body":{"type":"string","description":"The message text to send"}},"required":["conversationSid","body"]}""")]
    public async Task<object> SendConversationMessage(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.SendConversationMessage(serviceRequest.ConversationSid, serviceRequest.Body);
        await Log(LogLevel.Info, $"Message sent to conversation {serviceRequest.ConversationSid}");
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "list_conversation_messages")]
    [Description("Lists messages in a Twilio Conversation.")]
    [Parameters("""{"type":"object","properties":{"conversationSid":{"type":"string","description":"The SID of the conversation (starts with CH)"},"pageSize":{"type":"integer","description":"Number of messages to return. Defaults to 20."}},"required":["conversationSid"]}""")]
    public async Task<object> ListConversationMessages(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var client = CreateClient(config);
        var doc = await client.ListConversationMessages(serviceRequest.ConversationSid, serviceRequest.PageSize);
        return new { Success = true, Data = ToResult(doc) };
    }
}
