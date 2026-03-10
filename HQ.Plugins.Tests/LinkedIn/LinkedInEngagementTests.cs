using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using HQ.Plugins.LinkedIn;
using HQ.Plugins.LinkedIn.Models;

namespace HQ.Plugins.Tests.LinkedIn;

public class LinkedInEngagementTests
{
    private static readonly MethodInfo[] ServiceMethods = typeof(LinkedInService)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Where(m => m.GetParameters().Length == 2
                     && m.GetParameters()[0].ParameterType.Name == "ServiceConfig"
                     && m.GetParameters()[1].ParameterType.Name == "ServiceRequest")
        .ToArray();

    [Fact]
    public void LinkedInService_HasReactToPostMethod()
    {
        var method = ServiceMethods.FirstOrDefault(m =>
            m.GetCustomAttribute<DisplayAttribute>()?.Name == "react_to_post");
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void LinkedInService_HasCommentOnPostMethod()
    {
        var method = ServiceMethods.FirstOrDefault(m =>
            m.GetCustomAttribute<DisplayAttribute>()?.Name == "comment_on_post");
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void LinkedInService_HasRepostPostMethod()
    {
        var method = ServiceMethods.FirstOrDefault(m =>
            m.GetCustomAttribute<DisplayAttribute>()?.Name == "repost_post");
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void LinkedInService_HasGetPostCommentsMethod()
    {
        var method = ServiceMethods.FirstOrDefault(m =>
            m.GetCustomAttribute<DisplayAttribute>()?.Name == "get_post_comments");
        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
    }

    [Fact]
    public void LinkedInMethods_HasEngagementConstants()
    {
        Assert.Equal("react_to_post", LinkedInMethods.ReactToPost);
        Assert.Equal("comment_on_post", LinkedInMethods.CommentOnPost);
        Assert.Equal("repost_post", LinkedInMethods.RepostPost);
        Assert.Equal("get_post_comments", LinkedInMethods.GetPostComments);
    }

    [Fact]
    public void ServiceRequest_HasEngagementFields()
    {
        var request = new ServiceRequest
        {
            ReactionType = "LIKE",
            CommentText = "Great post!",
            OriginalPostUrn = "urn:li:share:123"
        };

        Assert.Equal("LIKE", request.ReactionType);
        Assert.Equal("Great post!", request.CommentText);
        Assert.Equal("urn:li:share:123", request.OriginalPostUrn);
    }

    [Fact]
    public void ServiceRequest_ReactionTypeDefaultsToNull()
    {
        var request = new ServiceRequest();
        Assert.Null(request.ReactionType);
    }

    [Fact]
    public void LinkedInService_TotalMethodCount_Is12()
    {
        // 8 original + 4 engagement = 12
        Assert.Equal(12, ServiceMethods.Length);
    }

    [Theory]
    [InlineData("react_to_post")]
    [InlineData("comment_on_post")]
    [InlineData("repost_post")]
    [InlineData("get_post_comments")]
    public void EngagementMethods_HaveParametersAttribute(string toolName)
    {
        var method = ServiceMethods.FirstOrDefault(m =>
            m.GetCustomAttribute<DisplayAttribute>()?.Name == toolName);
        Assert.NotNull(method);

        var paramsAttr = method.GetCustomAttribute<HQ.Models.Helpers.ParametersAttribute>();
        Assert.NotNull(paramsAttr);
        Assert.Contains("postUrn", paramsAttr.FunctionParameters);
    }
}
