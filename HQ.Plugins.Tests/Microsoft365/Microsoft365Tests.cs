using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.Microsoft365;
using HQ.Plugins.Microsoft365.Graph;

namespace HQ.Plugins.Tests.Microsoft365;

public class Microsoft365Tests
{
    private static IEnumerable<MethodInfo> GetToolMethods()
    {
        return typeof(Microsoft365Service).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));
    }

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = GetToolMethods().ToList();
        Assert.Equal(17, methods.Count);

        foreach (var method in methods)
        {
            var display = method.GetCustomAttribute<DisplayAttribute>();
            var desc = method.GetCustomAttribute<DescriptionAttribute>();
            var param = method.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();

            Assert.False(string.IsNullOrWhiteSpace(display?.Name), $"{method.Name} missing Display.Name");
            Assert.False(string.IsNullOrWhiteSpace(desc?.Description), $"{method.Name} missing Description");
            Assert.False(string.IsNullOrWhiteSpace(param?.FunctionParameters), $"{method.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(param!.FunctionParameters));
        }
    }

    [Theory]
    [InlineData("files_list")]
    [InlineData("files_upload")]
    [InlineData("excel_get_range")]
    [InlineData("excel_append_row")]
    [InlineData("word_create")]
    [InlineData("word_read")]
    public void ExposesExpectedTool(string toolName)
    {
        var names = GetToolMethods().Select(m => m.GetCustomAttribute<DisplayAttribute>()?.Name);
        Assert.Contains(toolName, names);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(701, "ZZ")]
    public void A1_ColumnLetter(int index, string expected)
    {
        Assert.Equal(expected, A1Helper.ColumnLetter(index));
    }

    [Theory]
    [InlineData("Sheet1!A1:C10", 11)]
    [InlineData("A1:C10", 11)]
    [InlineData("A5", 6)]
    [InlineData("", 1)]
    [InlineData(null, 1)]
    public void A1_NextRowFromUsedRange(string used, int expected)
    {
        Assert.Equal(expected, A1Helper.NextRowFromUsedRange(used));
    }

    [Theory]
    [InlineData(11, 1, 3, "A11:C11")]
    [InlineData(1, 2, 1, "A1:A2")]
    [InlineData(5, 3, 2, "A5:B7")]
    public void A1_BuildRangeAddress(int startRow, int rows, int cols, string expected)
    {
        Assert.Equal(expected, A1Helper.BuildRangeAddress(startRow, rows, cols));
    }

    [Fact]
    public void Docx_CreateAndReadRoundTrip()
    {
        const string text = "First line\nSecond line\nThird line";
        var bytes = DocxHelper.CreateDocx(text);

        Assert.NotEmpty(bytes);
        Assert.Equal(text, DocxHelper.ExtractText(bytes));
    }

    [Fact]
    public void Docx_HandlesEmptyText()
    {
        var bytes = DocxHelper.CreateDocx("");
        Assert.Equal("", DocxHelper.ExtractText(bytes));
    }
}
