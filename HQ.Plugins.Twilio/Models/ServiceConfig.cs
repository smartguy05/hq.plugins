using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.Twilio.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Twilio Account SID")]
    public string AccountSid { get; set; }

    [Tooltip("Twilio Auth Token")]
    public string AuthToken { get; set; }

    [Tooltip("Default Twilio phone number to send from (E.164 format, e.g. +15551234567)")]
    public string DefaultFromNumber { get; set; }

    [Tooltip("Twilio Verify Service SID for OTP verification (optional)")]
    public string VerifyServiceSid { get; set; }
}
