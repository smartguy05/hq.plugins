using HQ.Models.Attributes;
using HQ.Models.Interfaces;

namespace HQ.Plugins.ImageGeneration.Models;

public class ServiceConfig : IPluginConfig
{
    public string Name { get; set; }
    public string Description { get; set; }

    [Tooltip("Google Gemini API key for image generation")]
    public string ApiKey { get; set; }

    [Tooltip("Model ID to use, e.g. gemini-3.1-flash-image-preview or gemini-3-pro-image-preview")]
    public string Model { get; set; } = "gemini-3.1-flash-image-preview";

    [Tooltip("Directory where generated images are saved. Falls back to system temp directory if not set.")]
    public string OutputDirectory { get; set; }
}
