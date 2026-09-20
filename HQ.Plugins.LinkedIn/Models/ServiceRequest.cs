using HQ.Models.Interfaces;

namespace HQ.Plugins.LinkedIn.Models;

public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }

    /// <summary>
    /// WP6A-7 (third pass, 2026-09 security review): the caller's organization, for the
    /// production per-agent tool-call path (<see cref="HQ.Plugins.LinkedIn.LinkedInCommand.DoWork"/>).
    /// Not <c>[Injected]</c> (kept bindable, following the same convention as
    /// HQ.Plugins.FileStorage/HQ.Plugins.Tasks): <c>HQ.Services.Plugin.PluginService.InjectOrganizationId</c>
    /// force-overwrites this property's value server-side, on every tool call, with the calling
    /// agent's authoritative <c>agent.OrganizationId</c> before this plugin ever sees the call —
    /// any value the model supplied here is discarded. This is what gives
    /// <see cref="HQ.Plugins.LinkedIn.LinkedInCommand.GetBrowser"/> a trustworthy tenant identity
    /// to key the browser/profile cache by, closing the residual the earlier WP6A-7 passes left
    /// open ("the production per-agent path has no organization id available to it").
    /// </summary>
    public Guid? OrganizationId { get; set; }
}
