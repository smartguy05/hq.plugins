using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.UseMemos.Models;

public record ServiceConfig: IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Memos account credentials for reading and writing memos")]
    public MemoAccount MemoAccount { get; set; }
}
