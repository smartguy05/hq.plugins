using HQ.Models.Interfaces;

namespace HQ.Plugins.PythonRunner.Models;

public class ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
}