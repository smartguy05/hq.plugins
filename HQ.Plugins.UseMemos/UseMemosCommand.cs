using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.UseMemos.Models;

namespace HQ.Plugins.UseMemos;

public class UseMemosCommand: CommandBase<ServiceRequest,ServiceConfig>
{
    public override string Name => "Use Memos";
    public override string Description  => "Integration with UseMemos server";
    protected override INotificationService NotificationService { get; set; }

    private readonly string[] _validGetTypes = { "memos", "resources" };

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> enumerableToolCalls)
    {
        return await this.ProcessRequest(serviceRequest, config, NotificationService);
    }

    [Display(Name = "read_memos")]
    [Description("Reads memos or resources from the UseMemos server. Can retrieve all memos or a specific one by UID.")]
    [Parameters("""{"type":"object","properties":{"dataType":{"type":"string","description":"The type of data to read: 'memos' or 'resources'. Defaults to 'memos'."},"uid":{"type":"string","description":"Optional UID to retrieve a specific memo"}},"required":[]}""")]
    public async Task<object> ReadMemos(ServiceConfig config, ServiceRequest serviceRequest)
    {
        ValidateDataType(serviceRequest.DataType);
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.MemoAccount.ApiKey);
        try
        {
            var location = serviceRequest.DataType.ToLower();
            var url = $"{config.MemoAccount.MemosUrl}/api/v1/{location}";
            if (!string.IsNullOrWhiteSpace(serviceRequest.Uid))
            {
                url += $":by-uid/{serviceRequest.Uid}";
            }
            var response = await httpClient.GetAsync(new Uri(url));
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"An error occurred while attempting to get {serviceRequest.DataType}: {e}", e);
            throw;
        }
    }

    [Display(Name = "add_memo")]
    [Description("Creates a new memo on the UseMemos server. Requires user confirmation before saving.")]
    [Parameters("""{"type":"object","properties":{"content":{"type":"string","description":"The text content of the memo to create"},"visibility":{"type":"string","description":"Visibility level: 'PUBLIC', 'PROTECTED', or 'PRIVATE'. Defaults to 'PRIVATE'."}},"required":["content"]}""")]
    public async Task<object> AddMemo(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.ConfirmationId))
        {
            var confirmation = new Confirmation
            {
                ConfirmationMessage = "Are you sure you want to add this memo?",
                Content = serviceRequest.Content,
                Options = new Dictionary<string, bool>
                {
                    { "Yes", true },
                    { "No", false }
                },
                Id = Guid.NewGuid()
            };
            serviceRequest.ConfirmationId = confirmation.Id.ToString();
            var confirmationRequest = await NotificationService.RequestConfirmation(Name, confirmation, serviceRequest);
            var isSuccessful = (bool?)confirmationRequest.GetType().GetProperty("Success")?.GetValue(confirmationRequest) ?? false;
            if (isSuccessful)
            {
                return new
                {
                    Success = true,
                    ConfirmationId = confirmation.Id.ToString()
                };
            }

            return new
            {
                Success = false
            };
        }

        if (!NotificationService.DoesConfirmationExist(Guid.Parse(serviceRequest.ConfirmationId), out _))
        {
            return new
            {
                Success = false,
                Error = "Unable to add memo without valid confirmation"
            };
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.MemoAccount.ApiKey);

        var url = $"{config.MemoAccount.MemosUrl}/api/v1/memos";

        if (string.IsNullOrWhiteSpace(serviceRequest.Content))
        {
            throw new ArgumentException("Content cannot be empty for adding a memo.");
        }

        var visibility = !string.IsNullOrWhiteSpace(serviceRequest.Visibility) ? serviceRequest.Visibility.ToUpperInvariant() : "PRIVATE";

        var memoPayload = new Dictionary<string, object>
        {
            { "content", serviceRequest.Content },
            { "visibility", visibility }
        };

        var jsonPayload = JsonSerializer.Serialize(memoPayload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.PostAsync(new Uri(url), content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"An error occurred while attempting to add memo: {e}", e);
            throw;
        }
    }

    [Display(Name = "update_memo")]
    [Description("Updates an existing memo on the UseMemos server by its UID.")]
    [Parameters("""{"type":"object","properties":{"uid":{"type":"string","description":"The UID of the memo to update"},"content":{"type":"string","description":"The new content for the memo"},"visibility":{"type":"string","description":"Updated visibility level: 'PUBLIC', 'PROTECTED', or 'PRIVATE'"}},"required":["uid"]}""")]
    public async Task<object> UpdateMemo(ServiceConfig config, ServiceRequest serviceRequest)
    {
        if (string.IsNullOrWhiteSpace(serviceRequest.Uid))
        {
            throw new ArgumentException("Memo Uid (memoId) cannot be empty for updating a memo.");
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.MemoAccount.ApiKey);

        var url = $"{config.MemoAccount.MemosUrl}/api/v1/memos/{serviceRequest.Uid}";

        var memoPatchPayload = new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(serviceRequest.Content))
        {
            memoPatchPayload.Add("content", serviceRequest.Content);
        }

        if (!string.IsNullOrWhiteSpace(serviceRequest.Visibility))
        {
            memoPatchPayload.Add("visibility", serviceRequest.Visibility.ToUpperInvariant());
        }

        if (!memoPatchPayload.Any())
        {
            throw new ArgumentException("No fields provided to update for the memo.");
        }

        var jsonPayload = JsonSerializer.Serialize(memoPatchPayload);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), new Uri(url))
            {
                Content = content
            };
            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }
        catch (Exception e)
        {
            await Log(LogLevel.Error, $"An error occurred while attempting to update memo {serviceRequest.Uid}: {e}", e);
            throw;
        }
    }

    private void ValidateDataType(string method)
    {
        if (!_validGetTypes.Contains(method.ToLower()))
        {
            throw new Exception("Invalid Get method specified");
        }
    }
}
