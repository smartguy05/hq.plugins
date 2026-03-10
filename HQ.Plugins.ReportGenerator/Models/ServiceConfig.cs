using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.ReportGenerator.Models;

public record ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Directory where generated reports are saved, e.g. /data/reports")]
    public string OutputDirectory { get; set; }

    [Tooltip("Path to the HTML/Razor template file used for report generation")]
    public string TemplatePath { get; set; }
}
