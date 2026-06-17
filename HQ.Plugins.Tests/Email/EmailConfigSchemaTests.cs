using HQ.Models.Helpers;
using HQ.Plugins.Email.Models;

namespace HQ.Plugins.Tests.Email;

public class EmailConfigSchemaTests
{
    private static List<string> FieldNames()
    {
        return ConfigTemplateGenerator
            .GenerateSchema(typeof(ServiceConfig))
            .Select(f => f.Name)
            .ToList();
    }

    [Fact]
    public void Schema_DoesNotExposeChromaUrl()
    {
        // The internal Chroma URL is system-managed and must never appear in the
        // config UI — it is injected by the host at runtime.
        Assert.DoesNotContain("chromaUrl", FieldNames());
    }

    [Fact]
    public void Schema_DoesNotExposeChromaCollectionName()
    {
        // The collection name is auto-derived per-agent and must never be shown.
        Assert.DoesNotContain("chromaCollectionName", FieldNames());
    }

    [Fact]
    public void Schema_StillExposesUserEditableFields()
    {
        // Sanity: hiding the Chroma fields must not have hidden everything.
        var names = FieldNames();
        Assert.Contains("syncIntervalMinutes", names);
    }
}
