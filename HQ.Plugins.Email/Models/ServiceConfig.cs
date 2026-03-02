using HQ.Models.Interfaces;

namespace HQ.Plugins.Email.Models;

public record ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }
    public IEnumerable<EmailParameters> EmailAccounts { get; set; }
}