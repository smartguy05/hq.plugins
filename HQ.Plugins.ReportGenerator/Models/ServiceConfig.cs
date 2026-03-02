using HQ.Models.Interfaces;

namespace HQ.Plugins.ReportGenerator.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string OutputDirectory { get; set; }
    public string TemplatePath { get; set; }
}
