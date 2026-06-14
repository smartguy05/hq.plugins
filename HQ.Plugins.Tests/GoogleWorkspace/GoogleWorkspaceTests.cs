using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Google.Apis.Docs.v1.Data;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleWorkspace;
using HQ.Plugins.GoogleWorkspace.Clients;

namespace HQ.Plugins.Tests.GoogleWorkspace;

public class GoogleWorkspaceTests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(GoogleWorkspaceService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = GetToolMethods().ToList();
        Assert.Equal(20, methods.Count);

        foreach (var method in methods)
        {
            var display = method.GetCustomAttribute<DisplayAttribute>();
            var desc = method.GetCustomAttribute<DescriptionAttribute>();
            var param = method.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();

            Assert.False(string.IsNullOrWhiteSpace(display?.Name), $"{method.Name} missing Display.Name");
            Assert.False(string.IsNullOrWhiteSpace(desc?.Description), $"{method.Name} missing Description");
            Assert.False(string.IsNullOrWhiteSpace(param?.FunctionParameters), $"{method.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(param!.FunctionParameters)); // schema must be valid JSON
        }
    }

    [Theory]
    [InlineData("drive_list_files")]
    [InlineData("drive_upload_file")]
    [InlineData("docs_create")]
    [InlineData("docs_append_text")]
    [InlineData("sheets_get_values")]
    [InlineData("sheets_append_row")]
    public void ExposesExpectedTool(string toolName)
    {
        var names = GetToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name);
        Assert.Contains(toolName, names);
    }

    [Theory]
    [InlineData("application/vnd.google-apps.document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/vnd.google-apps.spreadsheet", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("application/vnd.google-apps.unknown", "application/pdf")]
    public void DefaultExportMime_MapsGoogleNativeTypes(string google, string expected)
    {
        Assert.Equal(expected, DriveClient.DefaultExportMime(google));
    }

    [Fact]
    public void NormalizeValues_ConvertsJsonLeavesToClrTypes()
    {
        var grid = JsonSerializer.Deserialize<List<List<JsonElement>>>("""[["name", 42, true, null]]""");
        var result = SheetsClient.NormalizeValues(grid);

        Assert.Single(result);
        Assert.Equal("name", result[0][0]);
        Assert.Equal(42L, result[0][1]);
        Assert.Equal(true, result[0][2]);
        Assert.Null(result[0][3]);
    }

    [Fact]
    public void NormalizeValues_HandlesNull() => Assert.Empty(SheetsClient.NormalizeValues(null));

    [Fact]
    public void DocsExtractText_FlattensParagraphs()
    {
        var body = new Body
        {
            Content =
            [
                new StructuralElement
                {
                    Paragraph = new Paragraph
                    {
                        Elements =
                        [
                            new ParagraphElement { TextRun = new TextRun { Content = "Hello " } },
                            new ParagraphElement { TextRun = new TextRun { Content = "world\n" } }
                        ]
                    }
                }
            ]
        };

        Assert.Equal("Hello world\n", DocsClient.ExtractText(body));
    }

    [Fact]
    public void DocsExtractText_HandlesEmptyBody() => Assert.Equal("", DocsClient.ExtractText(new Body()));
}
