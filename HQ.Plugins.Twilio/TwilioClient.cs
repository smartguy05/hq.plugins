using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HQ.Plugins.Twilio;

public class TwilioClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _accountSid;

    private const string BaseUrl = "https://api.twilio.com/2010-04-01";
    private const string LookupUrl = "https://lookups.twilio.com/v2";
    private const string VerifyUrl = "https://verify.twilio.com/v2";
    private const string ConversationsUrl = "https://conversations.twilio.com/v1";

    public TwilioClient(string accountSid, string authToken, HttpMessageHandler handler = null)
    {
        _accountSid = accountSid;
        _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    // ── Messaging ──────────────────────────────────────────────

    public Task<JsonDocument> SendSms(string from, string to, string body, string mediaUrl = null)
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = from,
            ["To"] = to,
            ["Body"] = body
        };
        if (!string.IsNullOrWhiteSpace(mediaUrl))
            form["MediaUrl"] = mediaUrl;

        return PostForm($"{BaseUrl}/Accounts/{_accountSid}/Messages.json", form);
    }

    public Task<JsonDocument> SendWhatsApp(string from, string to, string body, string mediaUrl = null)
    {
        return SendSms($"whatsapp:{from}", $"whatsapp:{to}", body, mediaUrl);
    }

    public Task<JsonDocument> GetMessage(string messageSid)
    {
        return Get($"{BaseUrl}/Accounts/{_accountSid}/Messages/{messageSid}.json");
    }

    public Task<JsonDocument> ListMessages(int pageSize = 20)
    {
        return Get($"{BaseUrl}/Accounts/{_accountSid}/Messages.json?PageSize={pageSize}");
    }

    // ── Voice ──────────────────────────────────────────────────

    public Task<JsonDocument> MakeCall(string from, string to, string twiml, bool record = false)
    {
        var form = new Dictionary<string, string>
        {
            ["From"] = from,
            ["To"] = to,
            ["Twiml"] = twiml
        };
        if (record)
            form["Record"] = "true";

        return PostForm($"{BaseUrl}/Accounts/{_accountSid}/Calls.json", form);
    }

    public Task<JsonDocument> GetCall(string callSid)
    {
        return Get($"{BaseUrl}/Accounts/{_accountSid}/Calls/{callSid}.json");
    }

    // ── Recordings ─────────────────────────────────────────────

    public Task<JsonDocument> ListRecordings(int pageSize = 20)
    {
        return Get($"{BaseUrl}/Accounts/{_accountSid}/Recordings.json?PageSize={pageSize}");
    }

    public Task<JsonDocument> GetRecording(string recordingSid)
    {
        return Get($"{BaseUrl}/Accounts/{_accountSid}/Recordings/{recordingSid}.json");
    }

    public Task<JsonDocument> DeleteRecording(string recordingSid)
    {
        return Delete($"{BaseUrl}/Accounts/{_accountSid}/Recordings/{recordingSid}.json");
    }

    // ── Lookup ─────────────────────────────────────────────────

    public Task<JsonDocument> LookupPhoneNumber(string phoneNumber)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        return Get($"{LookupUrl}/PhoneNumbers/{encoded}?Fields=line_type_intelligence,caller_name");
    }

    // ── Verify ─────────────────────────────────────────────────

    public Task<JsonDocument> SendVerification(string verifyServiceSid, string to, string channel = "sms")
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Channel"] = channel
        };
        return PostForm($"{VerifyUrl}/Services/{verifyServiceSid}/Verifications", form);
    }

    public Task<JsonDocument> CheckVerification(string verifyServiceSid, string to, string code)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Code"] = code
        };
        return PostForm($"{VerifyUrl}/Services/{verifyServiceSid}/VerificationCheck", form);
    }

    // ── Conversations ──────────────────────────────────────────

    public Task<JsonDocument> CreateConversation(string friendlyName = null)
    {
        var form = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(friendlyName))
            form["FriendlyName"] = friendlyName;
        return PostForm($"{ConversationsUrl}/Conversations", form);
    }

    public Task<JsonDocument> AddConversationParticipant(string conversationSid, string address, string proxyAddress)
    {
        var form = new Dictionary<string, string>
        {
            ["MessagingBinding.Address"] = address,
            ["MessagingBinding.ProxyAddress"] = proxyAddress
        };
        return PostForm($"{ConversationsUrl}/Conversations/{conversationSid}/Participants", form);
    }

    public Task<JsonDocument> SendConversationMessage(string conversationSid, string body)
    {
        var form = new Dictionary<string, string>
        {
            ["Body"] = body
        };
        return PostForm($"{ConversationsUrl}/Conversations/{conversationSid}/Messages", form);
    }

    public Task<JsonDocument> ListConversationMessages(string conversationSid, int pageSize = 20)
    {
        return Get($"{ConversationsUrl}/Conversations/{conversationSid}/Messages?PageSize={pageSize}");
    }

    // ── HTTP helpers ───────────────────────────────────────────

    private async Task<JsonDocument> PostForm(string url, Dictionary<string, string> form)
    {
        var response = await _http.PostAsync(url, new FormUrlEncodedContent(form));
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private async Task<JsonDocument> Get(string url)
    {
        var response = await _http.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private async Task<JsonDocument> Delete(string url)
    {
        var response = await _http.DeleteAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            return JsonDocument.Parse("{}");
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
