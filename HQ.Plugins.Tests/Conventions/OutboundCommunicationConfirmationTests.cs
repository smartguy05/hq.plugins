using HQ.Plugins.Twilio;

namespace HQ.Plugins.Tests.Conventions;

/// <summary>
/// WP6B-6 ratchet: outbound-communication tools (SMS/WhatsApp/voice) are fully LLM-controlled —
/// To, Body, MediaUrl and raw TwiML are ordinary tool arguments an attacker can steer via prompt
/// injection. Without [SupportsConfirmation] on the tool method, ConfirmationGate never fires
/// unless a TenantAdmin manually opts the tool into confirmationRequiredTools. This test asserts
/// the send-capable Twilio tools declare confirmation support so the host-side gate can enforce a
/// human-in-the-loop approval by default (config.RequiresConfirmation defaults to true).
/// </summary>
public class OutboundCommunicationConfirmationTests
{
    [Theory]
    [InlineData("send_sms")]
    [InlineData("send_whatsapp")]
    [InlineData("make_call")]
    public void TwilioSendTools_SupportConfirmation(string toolName)
    {
        var tools = new TwilioCommand().GetToolDefinitions();

        var tool = Assert.Single(tools, t => t.Function.Name == toolName);

        Assert.True(tool.Function.SupportsConfirmation,
            $"'{toolName}' is fully LLM-controlled and sends on the org's verified Twilio " +
            "identity — it must declare [SupportsConfirmation] so ConfirmationGate can enforce " +
            "human approval by default.");
    }
}
