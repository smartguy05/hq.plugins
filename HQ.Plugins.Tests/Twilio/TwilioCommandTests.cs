using System.Net;
using System.Text.Json;
using HQ.Models;
using HQ.Models.Interfaces;
using HQ.Plugins.Twilio;
using HQ.Plugins.Twilio.Models;
using Moq;
using Moq.Protected;

namespace HQ.Plugins.Tests.Twilio;

public class TwilioCommandTests
{
    private static ServiceConfig CreateConfig() => new()
    {
        AccountSid = "ACtest123",
        AuthToken = "testtoken",
        DefaultFromNumber = "+15550001111"
    };

    private static TwilioCommand CreateCommand(Mock<HttpMessageHandler> handler)
    {
        var command = new TwilioCommand();
        command.Logger = (_, _, _) => Task.CompletedTask;
        command.HttpHandler = handler.Object;
        return command;
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json")
            });
        return handler;
    }

    [Fact]
    public void GetToolDefinitions_ReturnsExpectedTools()
    {
        var command = new TwilioCommand();
        var tools = command.GetToolDefinitions();

        Assert.Contains(tools, t => t.Function.Name == "send_sms");
        Assert.Contains(tools, t => t.Function.Name == "send_whatsapp");
        Assert.Contains(tools, t => t.Function.Name == "make_call");
    }

    // ── SendSms ──────────────────────────────────────────────────

    [Fact]
    public async Task SendSms_SuccessResponse_ReturnsSidAndStatus()
    {
        var handler = CreateMockHandler("""{"sid":"SM123","status":"queued","error_code":null}""");
        var command = CreateCommand(handler);
        var config = CreateConfig();
        var request = new SendSmsArgs { To = "+15559999999", Body = "Hello" };

        var result = await command.SendSms(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
        Assert.Equal("SM123", doc.RootElement.GetProperty("MessageSid").GetString());
        Assert.Equal("queued", doc.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task SendSms_ErrorWithErrorCode_ReturnsFailure()
    {
        var handler = CreateMockHandler(
            """{"sid":"SM123","status":"failed","error_code":21211,"message":"Invalid To number"}""");
        var command = CreateCommand(handler);
        var config = CreateConfig();
        var request = new SendSmsArgs { To = "+15559999999", Body = "Hello" };

        var result = await command.SendSms(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
        Assert.Equal("Invalid To number", doc.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task SendSms_HttpErrorWithCodeNotErrorCode_ReturnsFailureInsteadOfThrowing()
    {
        // This is the bug: Twilio returns {"code":20003,...} without "error_code" or "sid"
        var handler = CreateMockHandler(
            """{"code":20003,"message":"Authentication Error","more_info":"https://twilio.com/docs/errors/20003","status":401}""",
            HttpStatusCode.Unauthorized);
        var command = CreateCommand(handler);
        var config = CreateConfig();
        var request = new SendSmsArgs { To = "+15559999999", Body = "Hello" };

        // After fix: should NOT throw KeyNotFoundException
        var result = await command.SendSms(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
        Assert.Contains("Authentication Error", doc.RootElement.GetProperty("Message").GetString());
    }

    [Fact]
    public async Task SendSms_ErrorResponseWithNoKnownFields_ReturnsGenericFailure()
    {
        // Edge case: completely unexpected response format
        var handler = CreateMockHandler("""{"unexpected":"data"}""", HttpStatusCode.InternalServerError);
        var command = CreateCommand(handler);
        var config = CreateConfig();
        var request = new SendSmsArgs { To = "+15559999999", Body = "Hello" };

        var result = await command.SendSms(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    // ── SendWhatsApp ──────────────────────────────────────────────

    [Fact]
    public async Task SendWhatsApp_HttpErrorWithCodeNotErrorCode_ReturnsFailure()
    {
        var handler = CreateMockHandler(
            """{"code":20003,"message":"Authentication Error","status":401}""",
            HttpStatusCode.Unauthorized);
        var command = CreateCommand(handler);
        var config = CreateConfig();
        var request = new SendWhatsAppArgs { To = "+15559999999", Body = "Hello" };

        var result = await command.SendWhatsApp(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    // ── MakeCall ──────────────────────────────────────────────────

    [Fact]
    public async Task MakeCall_SuccessResponse_ReturnsCallSid()
    {
        var handler = CreateMockHandler("""{"sid":"CA123","status":"queued","error_code":null}""");
        var command = CreateCommand(handler);
        var config = CreateConfig();
        var request = new MakeCallArgs { To = "+15559999999", Twiml = "<Response><Say>Hello</Say></Response>" };

        var result = await command.MakeCall(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
        Assert.Equal("CA123", doc.RootElement.GetProperty("CallSid").GetString());
    }

    [Fact]
    public async Task MakeCall_HttpErrorWithCodeNotErrorCode_ReturnsFailure()
    {
        var handler = CreateMockHandler(
            """{"code":20003,"message":"Authentication Error","status":401}""",
            HttpStatusCode.Unauthorized);
        var command = CreateCommand(handler);
        var config = CreateConfig();
        var request = new MakeCallArgs { To = "+15559999999", Twiml = "<Response><Say>Hello</Say></Response>" };

        var result = await command.MakeCall(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    // ── WP6B-6: outbound-communication confirmation gate ────────────────

    private static async Task<TwilioCommand> CreateCommandWithNotification(
        Mock<HttpMessageHandler> handler, Mock<INotificationService> notification)
    {
        var command = CreateCommand(handler);
        await command.Initialize("{}", (_, _, _) => Task.CompletedTask, notification.Object);
        return command;
    }

    [Fact]
    public async Task SendSms_RequestsConfirmation_WhenNoConfirmationIdSupplied()
    {
        var handler = CreateMockHandler("""{"sid":"SM123","status":"queued","error_code":null}""");
        var notification = new Mock<INotificationService>();
        notification
            .Setup(n => n.RequestConfirmation(
                It.IsAny<string>(), It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()))
            .ReturnsAsync(new { Success = true, AwaitingConfirmation = true });
        var command = await CreateCommandWithNotification(handler, notification);
        var config = CreateConfig(); // RequiresConfirmation defaults to true
        var request = new SendSmsArgs { To = "+15559999999", Body = "phish payload" };

        await command.SendSms(config, request);

        notification.Verify(n => n.RequestConfirmation(
            "Twilio",
            It.IsAny<Confirmation>(),
            It.IsAny<IPluginServiceRequest>()), Times.Once);
        handler.Protected().Verify("SendAsync", Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task SendSms_Executes_WhenConfirmationIdIsValid()
    {
        var handler = CreateMockHandler("""{"sid":"SM123","status":"queued","error_code":null}""");
        var notification = new Mock<INotificationService>();
        var confirmId = Guid.NewGuid();
        Confirmation outConf = null;
        notification.Setup(n => n.DoesConfirmationExist(confirmId, out outConf)).Returns(true);
        var command = await CreateCommandWithNotification(handler, notification);
        var config = CreateConfig();
        var request = new SendSmsArgs { To = "+15559999999", Body = "Hello", ConfirmationId = confirmId.ToString() };

        var result = await command.SendSms(config, request);

        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
    }

    [Fact]
    public async Task SendWhatsApp_RequestsConfirmation_WhenNoConfirmationIdSupplied()
    {
        var handler = CreateMockHandler("""{"sid":"SM123","status":"queued","error_code":null}""");
        var notification = new Mock<INotificationService>();
        notification
            .Setup(n => n.RequestConfirmation(
                It.IsAny<string>(), It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()))
            .ReturnsAsync(new { Success = true, AwaitingConfirmation = true });
        var command = await CreateCommandWithNotification(handler, notification);
        var config = CreateConfig();
        var request = new SendWhatsAppArgs { To = "+15559999999", Body = "phish payload" };

        await command.SendWhatsApp(config, request);

        notification.Verify(n => n.RequestConfirmation(
            "Twilio", It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()), Times.Once);
    }

    [Fact]
    public async Task MakeCall_RequestsConfirmation_WhenNoConfirmationIdSupplied()
    {
        var handler = CreateMockHandler("""{"sid":"CA123","status":"queued","error_code":null}""");
        var notification = new Mock<INotificationService>();
        notification
            .Setup(n => n.RequestConfirmation(
                It.IsAny<string>(), It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()))
            .ReturnsAsync(new { Success = true, AwaitingConfirmation = true });
        var command = await CreateCommandWithNotification(handler, notification);
        var config = CreateConfig();
        var request = new MakeCallArgs { To = "+15559999999", Twiml = "<Response><Say>hi</Say></Response>" };

        await command.MakeCall(config, request);

        notification.Verify(n => n.RequestConfirmation(
            "Twilio", It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()), Times.Once);
    }

    [Fact]
    public async Task SendSms_SkipsConfirmation_WhenRequiresConfirmationFalse()
    {
        var handler = CreateMockHandler("""{"sid":"SM123","status":"queued","error_code":null}""");
        var notification = new Mock<INotificationService>();
        var command = await CreateCommandWithNotification(handler, notification);
        var config = CreateConfig();
        config.RequiresConfirmation = false;
        var request = new SendSmsArgs { To = "+15559999999", Body = "Hello" };

        var result = await command.SendSms(config, request);

        notification.Verify(n => n.RequestConfirmation(
            It.IsAny<string>(), It.IsAny<Confirmation>(), It.IsAny<IPluginServiceRequest>()), Times.Never);
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("Success").GetBoolean());
    }
}
