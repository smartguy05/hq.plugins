using HQ.Models.Interfaces;

namespace HQ.Plugins.HeadlessBrowser.Models;

/// <summary>
/// Framework request envelope (the <c>T</c> in <c>CommandBase&lt;T, ServiceConfig&gt;</c>). Carries
/// only the orchestrator-supplied routing fields; per-tool LLM arguments now live on each tool's
/// dedicated args type (see <c>ToolArgs.cs</c>) and are bound by <c>ProcessRequest</c>.
/// </summary>
public record ServiceRequest : IPluginServiceRequest
{
    public string Method { get; set; }
    public string ToolCallId { get; set; }
    public string RequestingService { get; set; }
    public string ConfirmationId { get; set; }
}
