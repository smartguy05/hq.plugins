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
        return await this.ProcessRequest(RawServiceRequest, config, NotificationService);
    }

    private TwilioClient CreateClient(ServiceConfig config)
    {
        return new TwilioClient(config.AccountSid, config.AuthToken, HttpHandler);
    }

    private string ResolveFrom(ServiceConfig config, string from)
    {
        return !string.IsNullOrWhiteSpace(from) ? from : config.DefaultFromNumber;
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
    [Parameters(typeof(SendSmsArgs))]
    public async Task<object> SendSms(ServiceConfig config, SendSmsArgs request)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, request.From);
        var doc = await client.SendSms(from, request.To, request.Body, request.MediaUrl);

        if (IsErrorResponse(doc.RootElement, out var errorMessage))
        {
            await Log(LogLevel.Warning, $"SMS send failed: {errorMessage}");
            return new { Success = false, Message = errorMessage };
        }

        await Log(LogLevel.Info, $"SMS sent to {request.To}");
        var sid = doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        var status = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        return new { Success = true, MessageSid = sid, Status = status };
    }

    [Display(Name = "send_whatsapp")]
    [Description("Sends a WhatsApp message to a phone number via Twilio.")]
    [Parameters(typeof(SendWhatsAppArgs))]
    public async Task<object> SendWhatsApp(ServiceConfig config, SendWhatsAppArgs request)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, request.From);
        var doc = await client.SendWhatsApp(from, request.To, request.Body, request.MediaUrl);

        if (IsErrorResponse(doc.RootElement, out var errorMessage))
        {
            await Log(LogLevel.Warning, $"WhatsApp send failed: {errorMessage}");
            return new { Success = false, Message = errorMessage };
        }

        await Log(LogLevel.Info, $"WhatsApp message sent to {request.To}");
        var sid = doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        var status = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        return new { Success = true, MessageSid = sid, Status = status };
    }

    [Display(Name = "get_message")]
    [Description("Gets details and delivery status of a specific SMS/MMS message by its SID.")]
    [Parameters(typeof(GetMessageArgs))]
    public async Task<object> GetMessage(ServiceConfig config, GetMessageArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.GetMessage(request.MessageSid);
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "list_messages")]
    [Description("Lists recent SMS/MMS messages on the account.")]
    [Parameters(typeof(ListMessagesArgs))]
    public async Task<object> ListMessages(ServiceConfig config, ListMessagesArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.ListMessages(request.PageSize);
        return new { Success = true, Data = ToResult(doc) };
    }

    // ── Voice ──────────────────────────────────────────────────

    [Display(Name = "make_call")]
    [Description("Initiates an outbound phone call. Use TwiML to control what happens on the call (e.g. <Say> for text-to-speech, <Play> for audio, <Gather> for input).")]
    [Parameters(typeof(MakeCallArgs))]
    public async Task<object> MakeCall(ServiceConfig config, MakeCallArgs request)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, request.From);
        var doc = await client.MakeCall(from, request.To, request.Twiml, request.Record);

        if (IsErrorResponse(doc.RootElement, out var errorMessage))
        {
            await Log(LogLevel.Warning, $"Call failed: {errorMessage}");
            return new { Success = false, Message = errorMessage };
        }

        await Log(LogLevel.Info, $"Call initiated to {request.To}");
        var sid = doc.RootElement.TryGetProperty("sid", out var sidEl) ? sidEl.GetString() : null;
        var status = doc.RootElement.TryGetProperty("status", out var statusEl) ? statusEl.GetString() : null;
        return new { Success = true, CallSid = sid, Status = status };
    }

    [Display(Name = "get_call")]
    [Description("Gets details and status of a specific phone call by its SID.")]
    [Parameters(typeof(GetCallArgs))]
    public async Task<object> GetCall(ServiceConfig config, GetCallArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.GetCall(request.CallSid);
        return new { Success = true, Data = ToResult(doc) };
    }

    // ── Recordings ─────────────────────────────────────────────

    [Display(Name = "list_recordings")]
    [Description("Lists call recordings on the account.")]
    [Parameters(typeof(ListRecordingsArgs))]
    public async Task<object> ListRecordings(ServiceConfig config, ListRecordingsArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.ListRecordings(request.PageSize);
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "get_recording")]
    [Description("Gets metadata for a specific call recording by its SID.")]
    [Parameters(typeof(GetRecordingArgs))]
    public async Task<object> GetRecording(ServiceConfig config, GetRecordingArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.GetRecording(request.RecordingSid);
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "delete_recording")]
    [Description("Deletes a call recording by its SID.")]
    [Parameters(typeof(DeleteRecordingArgs))]
    public async Task<object> DeleteRecording(ServiceConfig config, DeleteRecordingArgs request)
    {
        using var client = CreateClient(config);
        await client.DeleteRecording(request.RecordingSid);
        await Log(LogLevel.Info, $"Recording {request.RecordingSid} deleted");
        return new { Success = true, Message = $"Recording {request.RecordingSid} deleted" };
    }

    // ── Lookup ─────────────────────────────────────────────────

    [Display(Name = "lookup_phone_number")]
    [Description("Looks up information about a phone number including carrier, line type (mobile/landline/VoIP), and caller name.")]
    [Parameters(typeof(LookupPhoneNumberArgs))]
    public async Task<object> LookupPhoneNumber(ServiceConfig config, LookupPhoneNumberArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.LookupPhoneNumber(request.PhoneNumber);
        await Log(LogLevel.Info, $"Looked up {request.PhoneNumber}");
        return new { Success = true, Data = ToResult(doc) };
    }

    // ── Verify ─────────────────────────────────────────────────

    [Display(Name = "send_verification")]
    [Description("Sends a verification code (OTP) to a phone number or email via Twilio Verify.")]
    [Parameters(typeof(SendVerificationArgs))]
    public async Task<object> SendVerification(ServiceConfig config, SendVerificationArgs request)
    {
        if (string.IsNullOrWhiteSpace(config.VerifyServiceSid))
        {
            return new { Success = false, Message = "Verify Service SID is not configured" };
        }

        using var client = CreateClient(config);
        var doc = await client.SendVerification(config.VerifyServiceSid, request.To, request.Channel);

        if (doc.RootElement.TryGetProperty("status", out var status) && status.GetString() == "pending")
        {
            await Log(LogLevel.Info, $"Verification sent to {request.To} via {request.Channel}");
            return new { Success = true, Status = "pending", Message = $"Verification code sent to {request.To}" };
        }

        return new { Success = false, Data = ToResult(doc) };
    }

    [Display(Name = "check_verification")]
    [Description("Checks a verification code (OTP) submitted by a user against Twilio Verify.")]
    [Parameters(typeof(CheckVerificationArgs))]
    public async Task<object> CheckVerification(ServiceConfig config, CheckVerificationArgs request)
    {
        if (string.IsNullOrWhiteSpace(config.VerifyServiceSid))
        {
            return new { Success = false, Message = "Verify Service SID is not configured" };
        }

        using var client = CreateClient(config);
        var doc = await client.CheckVerification(config.VerifyServiceSid, request.To, request.Code);

        var verifyStatus = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : "unknown";
        var approved = verifyStatus == "approved";

        await Log(LogLevel.Info, $"Verification check for {request.To}: {verifyStatus}");
        return new { Success = approved, Status = verifyStatus, Valid = approved };
    }

    // ── Conversations ──────────────────────────────────────────

    [Display(Name = "create_conversation")]
    [Description("Creates a new Twilio Conversation for multi-party messaging across SMS, WhatsApp, and chat.")]
    [Parameters(typeof(CreateConversationArgs))]
    public async Task<object> CreateConversation(ServiceConfig config, CreateConversationArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.CreateConversation(request.FriendlyName);

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
    [Parameters(typeof(AddConversationParticipantArgs))]
    public async Task<object> AddConversationParticipant(ServiceConfig config, AddConversationParticipantArgs request)
    {
        using var client = CreateClient(config);
        var from = ResolveFrom(config, request.From);
        var doc = await client.AddConversationParticipant(request.ConversationSid, request.ParticipantAddress, from);
        await Log(LogLevel.Info, $"Participant {request.ParticipantAddress} added to {request.ConversationSid}");
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "send_conversation_message")]
    [Description("Sends a message to all participants in a Twilio Conversation.")]
    [Parameters(typeof(SendConversationMessageArgs))]
    public async Task<object> SendConversationMessage(ServiceConfig config, SendConversationMessageArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.SendConversationMessage(request.ConversationSid, request.Body);
        await Log(LogLevel.Info, $"Message sent to conversation {request.ConversationSid}");
        return new { Success = true, Data = ToResult(doc) };
    }

    [Display(Name = "list_conversation_messages")]
    [Description("Lists messages in a Twilio Conversation.")]
    [Parameters(typeof(ListConversationMessagesArgs))]
    public async Task<object> ListConversationMessages(ServiceConfig config, ListConversationMessagesArgs request)
    {
        using var client = CreateClient(config);
        var doc = await client.ListConversationMessages(request.ConversationSid, request.PageSize);
        return new { Success = true, Data = ToResult(doc) };
    }
}
