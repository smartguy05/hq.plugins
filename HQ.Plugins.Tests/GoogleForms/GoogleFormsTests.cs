using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using HQ.Models.Interfaces;
using HQ.Plugins.GoogleForms;
using HQ.Plugins.GoogleForms.Models;

namespace HQ.Plugins.Tests.GoogleForms;

public class GoogleFormsTests
{
    private static IEnumerable<MethodInfo> ToolMethods() =>
        typeof(GoogleFormsService).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 2 &&
                        typeof(IPluginConfig).IsAssignableFrom(m.GetParameters()[0].ParameterType) &&
                        typeof(IPluginServiceRequest).IsAssignableFrom(m.GetParameters()[1].ParameterType));

    [Fact]
    public void AllToolMethods_HaveCompleteAnnotations()
    {
        var methods = ToolMethods().ToList();
        Assert.Equal(5, methods.Count);
        foreach (var m in methods)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DisplayAttribute>()?.Name), $"{m.Name} missing Display.Name");
            Assert.False(string.IsNullOrWhiteSpace(m.GetCustomAttribute<DescriptionAttribute>()?.Description), $"{m.Name} missing Description");
            var p = m.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();
            Assert.False(string.IsNullOrWhiteSpace(p?.FunctionParameters), $"{m.Name} missing Parameters");
            Assert.NotNull(JsonDocument.Parse(p!.FunctionParameters));
        }
    }

    [Fact]
    public void BuildItem_TextQuestion()
    {
        var item = GoogleFormsService.BuildItem(new QuestionSpec { Title = "Name", Type = "TEXT" });
        Assert.Equal("Name", item.Title);
        Assert.NotNull(item.QuestionItem.Question.TextQuestion);
        Assert.False(item.QuestionItem.Question.TextQuestion.Paragraph);
    }

    [Fact]
    public void BuildItem_ParagraphQuestion()
    {
        var item = GoogleFormsService.BuildItem(new QuestionSpec { Title = "Feedback", Type = "PARAGRAPH" });
        Assert.True(item.QuestionItem.Question.TextQuestion.Paragraph);
    }

    [Fact]
    public void BuildItem_DropdownMapsToDropDownWithOptions()
    {
        var item = GoogleFormsService.BuildItem(new QuestionSpec { Title = "Pick", Type = "DROPDOWN", Options = ["A", "B"], Required = true });
        Assert.Equal("DROP_DOWN", item.QuestionItem.Question.ChoiceQuestion.Type);
        Assert.Equal(2, item.QuestionItem.Question.ChoiceQuestion.Options.Count);
        Assert.True(item.QuestionItem.Question.Required);
    }
}
