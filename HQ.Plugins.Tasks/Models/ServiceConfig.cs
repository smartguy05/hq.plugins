using System;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Tasks.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; } = "Tasks";
    public string Description { get; set; } = "Task manager with projects, tasks, comments.";

    /// <summary>
    /// Injected by the host during initialization — identifies the agent that owns this plugin
    /// config. Used to scope project-less tasks to the calling agent.
    /// </summary>
    public Guid? AgentId { get; set; }
    public string AgentName { get; set; }

    // Otherwise empty by design — the plugin uses HQ's shared Postgres connection via IDatabasePlugin.
}
