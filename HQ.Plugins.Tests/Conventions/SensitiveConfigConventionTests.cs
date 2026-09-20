using System.Reflection;
using System.Text.RegularExpressions;
using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Tests.Conventions;

/// <summary>
/// WP6B-7 / WP6A-11 ratchet: [Sensitive] is the only signal that drives both the config UI's
/// password-style masking (<see cref="HQ.Models.Helpers.ConfigTemplateGenerator"/>) and the
/// tool-setup assistant's auth_kind / hasSecretField detection in PluginService — a config
/// property that "looks like" a credential but isn't marked [Sensitive] renders as a plaintext
/// input and gets treated as secret-free, so the setup assistant will happily accept it pasted
/// into chat (persisted into the conversation, trace store and debug log) instead of routing it
/// through collect_secret. This scans every IPluginConfig implementation shipped by this repo's
/// plugin assemblies and fails for any string property whose name ends in Token/Secret/Key/
/// Password/Pat that does not carry [Sensitive].
/// </summary>
public class SensitiveConfigConventionTests
{
    private static readonly Regex CredentialLikeName =
        new(@"(Token|Secret|Key|Password|Pat)$", RegexOptions.Compiled);

    private static IEnumerable<Type> PluginConfigTypes()
    {
        // The test project references every plugin project, so all plugin DLLs land in the
        // test output directory (mirrors ExternalContentConventionTests.PluginAssemblyNames).
        var dlls = Directory.GetFiles(AppContext.BaseDirectory, "HQ.Plugins.*.dll")
            .Where(f => !Path.GetFileNameWithoutExtension(f)
                .Equals("HQ.Plugins.Tests", StringComparison.OrdinalIgnoreCase));

        foreach (var dll in dlls)
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(dll);
            }
            catch
            {
                continue;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                if (type is { IsClass: true, IsAbstract: false } && typeof(IPluginConfig).IsAssignableFrom(type))
                    yield return type;
            }
        }
    }

    [Fact]
    public void CredentialShapedConfigProperties_AreMarkedSensitive()
    {
        var configTypes = PluginConfigTypes().ToList();
        Assert.NotEmpty(configTypes);

        var violations = new List<string>();
        foreach (var type in configTypes)
        {
            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType != typeof(string))
                    continue;
                if (!CredentialLikeName.IsMatch(prop.Name))
                    continue;
                if (prop.GetCustomAttribute<SensitiveAttribute>() == null)
                    violations.Add($"{type.FullName}.{prop.Name}");
            }
        }

        Assert.True(violations.Count == 0,
            "These IPluginConfig properties look like credentials (name ends with Token/Secret/" +
            "Key/Password/Pat) but are missing [Sensitive], so they render as plaintext in the " +
            "config UI and the setup assistant will not route them through collect_secret:\n" +
            string.Join("\n", violations));
    }
}
