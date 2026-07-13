using System.Reflection;

namespace HQ.Plugins.Tests.Conventions;

/// <summary>
/// SAFE-02 ratchet: enforces that every plugin assembly is categorized in
/// <see cref="ExternalContentManifest"/> and that every plugin claiming to wrap external
/// content actually ships provenance tests. A brand-new plugin fails here until its
/// author decides — and proves — how its output is classified.
/// </summary>
public class ExternalContentConventionTests
{
    private static List<string> PluginAssemblyNames()
    {
        // The test project references every plugin project, so all plugin DLLs land in
        // the test output directory.
        return Directory.GetFiles(AppContext.BaseDirectory, "HQ.Plugins.*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.Equals(n, "HQ.Plugins.Tests", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [Fact]
    public void EveryPluginAssembly_IsCategorizedInManifest_ExactlyOnce()
    {
        var plugins = PluginAssemblyNames();
        Assert.NotEmpty(plugins);

        var uncategorized = plugins
            .Where(p => !ExternalContentManifest.WrappedExternalContent.Contains(p)
                     && !ExternalContentManifest.InternalOrStructured.Contains(p))
            .ToList();
        Assert.True(uncategorized.Count == 0,
            "Uncategorized plugins (add to ExternalContentManifest — wrapped or exempt-with-reason): "
            + string.Join(", ", uncategorized));

        var doubleCategorized = ExternalContentManifest.WrappedExternalContent
            .Intersect(ExternalContentManifest.InternalOrStructured, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(doubleCategorized.Count == 0,
            "Plugins in BOTH manifest sets: " + string.Join(", ", doubleCategorized));

        var stale = ExternalContentManifest.WrappedExternalContent
            .Concat(ExternalContentManifest.InternalOrStructured)
            .Where(m => !plugins.Contains(m, StringComparer.OrdinalIgnoreCase))
            .ToList();
        Assert.True(stale.Count == 0,
            "Manifest entries with no matching plugin assembly (remove them): "
            + string.Join(", ", stale));
    }

    [Fact]
    public void WrappedPlugins_HaveProvenanceTestClass()
    {
        var testTypes = typeof(ExternalContentConventionTests).Assembly.GetTypes();

        var missing = new List<string>();
        foreach (var plugin in ExternalContentManifest.WrappedExternalContent.OrderBy(p => p))
        {
            var shortName = plugin.Replace("HQ.Plugins.", "");
            var expectedClass = $"{shortName}ProvenanceTests";

            // Accept e.g. EmailServiceProvenanceTests for HQ.Plugins.Email: the class must
            // start with the plugin's short name and end with "ProvenanceTests".
            var testClass = testTypes.FirstOrDefault(t =>
                t.IsClass && !t.IsAbstract &&
                t.Name.StartsWith(shortName, StringComparison.Ordinal) &&
                t.Name.EndsWith("ProvenanceTests", StringComparison.Ordinal));

            var hasTests = testClass != null && testClass
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Any(m => m.GetCustomAttributes().Any(a => a is FactAttribute or TheoryAttribute));

            if (!hasTests)
                missing.Add($"{plugin} (expected test class '{expectedClass}' with at least one [Fact]/[Theory])");
        }

        Assert.True(missing.Count == 0,
            "Wrapped plugins without provenance tests:\n" + string.Join("\n", missing));
    }
}
