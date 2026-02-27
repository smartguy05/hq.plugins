using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.HomeAssistantVoice.Models;

namespace HQ.Plugins.HomeAssistantVoice;

public class HomeAssistantAssistCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "HQ.Plugins.HomeAssistantAssist";
    public override string Description => "A plugin to send natural language commands to Home Assistant";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(serviceRequest, config, NotificationService);
    }

    [Display(Name = "home_assistant_command")]
    [Description("Sends a natural language command to Home Assistant to control smart home devices")]
    [Parameters("""{"type":"object","properties":{"query":{"type":"string","description":"The natural language command to send to Home Assistant"}},"required":["query"]}""")]
    public async Task<object> HomeAssistantCommand(ServiceConfig config, ServiceRequest serviceRequest)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(config.HomeAssistApiKey))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.HomeAssistApiKey);
        }

        var body = new
        {
            text = serviceRequest.Query,
            language = "en"
        };
        var jsonBody = JsonSerializer.Serialize(body);
        var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(config.HomeAssistUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            await Log(LogLevel.Warning, "Unable to execute Home Assistant command");
            await Log(LogLevel.Info, await response.Content.ReadAsStringAsync());
            return new
            {
                Success = false
            };
        }

        return await response.Content.ReadAsStringAsync();
    }
}
