using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Safety;
using HQ.Models.Tools;
using HQ.Plugins.HomeAssistantVoice.Models;

namespace HQ.Plugins.HomeAssistantVoice;

public class HomeAssistantAssistCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Home Assistant Assist";
    public override string Description => "A plugin to send natural language commands to Home Assistant";
    protected override INotificationService NotificationService { get; set; }

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(RawServiceRequest, config, NotificationService);
    }

    /// <summary>
    /// Test-only DNS resolver override for the SSRF guard below (adversarial-review
    /// follow-up to WP6B-10). Production leaves this null and gets real
    /// <see cref="Dns.GetHostAddresses(string)"/> resolution, matching the precedent set
    /// by HQ.Services.Utility.UrlValidationService / HQ.Services.BrowserLoginService.
    /// </summary>
    internal Func<string, IEnumerable<IPAddress>> ResolveHostForTesting { get; set; }

    /// <summary>
    /// Test-only override for the operator allow-list below (second-round adversarial-review
    /// follow-up). Production leaves this null and reads <see cref="AllowedHostsEnvironmentVariable"/>.
    /// </summary>
    internal IEnumerable<string> AllowedHostsForTesting { get; set; }

    /// <summary>
    /// Comma-separated allow-list of exact host names, read once per call — the operator
    /// escape hatch for a deliberately-configured internal/self-hosted HomeAssistUrl.
    /// Mirrors <c>HQ.Services.Utility.UrlValidationService.AllowedHostsEnvironmentVariable</c>,
    /// which exists for precisely this "self-hosted target" case (there: webhooks) and uses
    /// the same environment-variable name, so an operator who already allow-lists their LAN
    /// device for webhooks does not need a second, plugin-specific variable.
    /// </summary>
    public const string AllowedHostsEnvironmentVariable = "URL_VALIDATION_ALLOWED_HOSTS";

    private static IEnumerable<string> ReadAllowedHostsFromEnvironment()
    {
        var raw = Environment.GetEnvironmentVariable(AllowedHostsEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Builds the <see cref="UrlGuardOptions"/> used to validate <see cref="ServiceConfig.HomeAssistUrl"/>.
    /// Second-round adversarial review: UrlGuard.IsAddressNavigable unconditionally rejects ALL
    /// of RFC1918 (10/8, 172.16/12, 192.168/16), which is Home Assistant's own documented,
    /// overwhelmingly typical deployment (ServiceConfig.cs's Tooltip gives
    /// "http://192.168.1.100:8123" as the canonical example) — the DNS-resolution fix below
    /// closed the SSRF bypass but, with no escape hatch, also permanently disabled the plugin
    /// for essentially every real self-hosted user. <see cref="AllowedHosts"/> gives the
    /// operator who deliberately configured that internal target a way to allow it explicitly,
    /// the same trade-off HQ.Services.Utility.UrlValidationService already makes for webhooks.
    /// </summary>
    internal UrlGuardOptions BuildUrlGuardOptionsForTesting() => BuildUrlGuardOptions();

    private UrlGuardOptions BuildUrlGuardOptions() => new()
    {
        ResolveHost = ResolveHostForTesting ?? Dns.GetHostAddresses,
        AllowedHosts = AllowedHostsForTesting ?? ReadAllowedHostsFromEnvironment()
    };

    [Display(Name = "home_assistant_command")]
    [Description("Sends a natural language command to Home Assistant to control smart home devices")]
    [Parameters(typeof(HomeAssistantCommandArgs))]
    public async Task<object> HomeAssistantCommand(ServiceConfig config, HomeAssistantCommandArgs serviceRequest)
    {
        // WP6B-10: HomeAssistUrl is a TenantAdmin-set config value, not an LLM argument,
        // but nothing validated it before this plugin attached the long-lived HA bearer
        // token and POSTed there — a config pointed at an internal/loopback/metadata
        // address turns this into an SSRF primitive with credentials attached. Reject
        // before creating the HttpClient (and before the token ever leaves the process)
        // instead of finding out from a failed/successful connection.
        //
        // Adversarial-review follow-up: the original fix only rejected literal IPs and a
        // handful of hardcoded hostnames — an attacker-controlled domain name resolving to
        // an internal/loopback/metadata address sailed through untouched. UrlGuardOptions.
        // ResolveHost now wires in real DNS resolution so the resolved address(es) get the
        // same checks a literal IP would.
        //
        // Second-round adversarial review: see BuildUrlGuardOptions() above — AllowedHosts
        // gives operators of a real self-hosted instance an escape hatch from the RFC1918
        // block that DNS resolution now enforces unconditionally.
        var urlGuardOptions = BuildUrlGuardOptions();
        if (!UrlGuard.IsNavigable(config?.HomeAssistUrl, out var urlReason, urlGuardOptions))
        {
            await Log(LogLevel.Warning, $"Refusing Home Assistant request: {urlReason}");
            return new
            {
                Success = false,
                Error = $"Home Assistant URL is not allowed: {urlReason}"
            };
        }

        // Minor adversarial-review note: the configured HomeAssistUrl is TenantAdmin-trusted
        // content, not attacker-served on the happy path, but UrlGuard's own docs warn that a
        // first-hop-clean URL can still redirect into a blocked address. Disable auto-redirect
        // instead of following (and re-validating) each hop, since Home Assistant's Assist API
        // has no legitimate reason to redirect.
        using var httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
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
