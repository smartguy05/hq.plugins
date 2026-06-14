using HQ.Models.Interfaces;

namespace HQ.Plugins.Tasks.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; } = "Tasks";
    public string Description { get; set; } = "Task manager with projects, tasks, comments.";

    // Empty by design — the plugin uses HQ's shared Postgres connection via IDatabasePlugin.
}
