using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HQ.Models.Enums;
using HQ.Models.Extensions;
using HQ.Models.Helpers;
using HQ.Models.Interfaces;
using HQ.Models.Tools;
using HQ.Plugins.ImageGeneration.Models;

namespace HQ.Plugins.ImageGeneration;

public class ImageGenerationCommand : CommandBase<ServiceRequest, ServiceConfig>
{
    public override string Name => "Image Generation";
    public override string Description => "A plugin to generate images using Google Gemini image generation models";
    protected override INotificationService NotificationService { get; set; }

    private static readonly HttpClient HttpClient = new();

    public override List<ToolCall> GetToolDefinitions()
    {
        return this.GetServiceToolCalls();
    }

    protected override async Task<object> DoWork(ServiceRequest serviceRequest, ServiceConfig config, IEnumerable<ToolCall> availableToolCalls)
    {
        return await this.ProcessRequest(RawServiceRequest, config, NotificationService);
    }

    [Display(Name = "generate_image")]
    [Description("Generates an image from a text prompt using Google Gemini image generation.")]
    [Parameters(typeof(GenerateImageArgs))]
    public async Task<object> GenerateImage(ServiceConfig config, GenerateImageArgs request)
    {
        return await CallGeminiImageApi(config, request.Prompt, request.AspectRatio, request.Resolution, request.OutputFileName, null);
    }

    [Display(Name = "describe_image")]
    [Description("Analyzes an image and returns a detailed text description of its contents.")]
    [Parameters(typeof(DescribeImageArgs))]
    public async Task<object> DescribeImage(ServiceConfig config, DescribeImageArgs request)
    {
        return await CallGeminiDescribeApi(config, request.ReferenceImage, request.Prompt);
    }

    [Display(Name = "edit_image")]
    [Description("Edits or transforms an existing image based on a text prompt using Google Gemini image generation.")]
    [Parameters(typeof(EditImageArgs))]
    public async Task<object> EditImage(ServiceConfig config, EditImageArgs request)
    {
        return await CallGeminiImageApi(config, request.Prompt, request.AspectRatio, request.Resolution, request.OutputFileName, request.ReferenceImage);
    }

    private async Task<object> CallGeminiDescribeApi(ServiceConfig config, string referenceImage, string promptInput)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            await Log(LogLevel.Warning, "Image generation API key is not configured");
            return new { Success = false, Message = "API key is not configured" };
        }

        if (string.IsNullOrWhiteSpace(referenceImage))
        {
            return new { Success = false, Message = "Reference image is required" };
        }

        var model = string.IsNullOrWhiteSpace(config.Model) ? "gemini-3.1-flash-image-preview" : config.Model;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var prompt = string.IsNullOrWhiteSpace(promptInput)
            ? "Describe this image in detail."
            : promptInput;

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inline_data = new
                            {
                                mime_type = "image/png",
                                data = referenceImage
                            }
                        },
                        new { text = prompt }
                    }
                }
            },
            generationConfig = new
            {
                responseModalities = new[] { "TEXT" }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", config.ApiKey);

        try
        {
            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                await Log(LogLevel.Warning, $"Describe image API returned {response.StatusCode}");
                await Log(LogLevel.Info, errorBody);
                return new { Success = false, Message = $"API error: {response.StatusCode}" };
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var candidates = doc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0)
            {
                return new { Success = false, Message = "No description was generated" };
            }

            var responseParts = candidates[0].GetProperty("content").GetProperty("parts");
            foreach (var part in responseParts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement))
                {
                    var description = textElement.GetString();
                    await Log(LogLevel.Info, "Image described successfully");
                    return new
                    {
                        Success = true,
                        Description = description,
                        Message = "Image described successfully"
                    };
                }
            }

            return new { Success = false, Message = "No text found in response" };
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Image description failed: {ex.Message}");
            return new { Success = false, Message = $"Image description failed: {ex.Message}" };
        }
    }

    private async Task<object> CallGeminiImageApi(ServiceConfig config, string promptInput, string aspectRatio, string resolution, string outputFileName, string referenceImageBase64)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            await Log(LogLevel.Warning, "Image generation API key is not configured");
            return new { Success = false, Message = "API key is not configured" };
        }

        if (string.IsNullOrWhiteSpace(promptInput))
        {
            return new { Success = false, Message = "Prompt is required" };
        }

        var model = string.IsNullOrWhiteSpace(config.Model) ? "gemini-3.1-flash-image-preview" : config.Model;
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var parts = new List<object>();

        // Add reference image if provided (for edit_image)
        if (!string.IsNullOrWhiteSpace(referenceImageBase64))
        {
            parts.Add(new
            {
                inline_data = new
                {
                    mime_type = "image/png",
                    data = referenceImageBase64
                }
            });
        }

        parts.Add(new { text = promptInput });

        var requestBody = new
        {
            contents = new[]
            {
                new { parts }
            },
            generationConfig = new
            {
                responseModalities = new[] { "IMAGE" },
                aspectRatio = aspectRatio ?? "1:1",
                resolution = resolution ?? "1K"
            }
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-goog-api-key", config.ApiKey);

        try
        {
            var response = await HttpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                await Log(LogLevel.Warning, $"Image generation API returned {response.StatusCode}");
                await Log(LogLevel.Info, errorBody);
                return new { Success = false, Message = $"API error: {response.StatusCode}" };
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);

            var candidates = doc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0)
            {
                return new { Success = false, Message = "No image was generated" };
            }

            var responseParts = candidates[0].GetProperty("content").GetProperty("parts");
            foreach (var part in responseParts.EnumerateArray())
            {
                if (part.TryGetProperty("inline_data", out var inlineData))
                {
                    var mimeType = inlineData.GetProperty("mimeType").GetString() ?? "image/png";
                    var base64Data = inlineData.GetProperty("data").GetString();

                    var filePath = SaveImage(config, outputFileName, base64Data, mimeType);

                    await Log(LogLevel.Info, $"Image generated and saved to {filePath}");

                    return new
                    {
                        Success = true,
                        FilePath = filePath,
                        Base64Data = base64Data,
                        MimeType = mimeType,
                        Message = $"Image generated successfully and saved to {filePath}"
                    };
                }
            }

            return new { Success = false, Message = "No image data found in response" };
        }
        catch (Exception ex)
        {
            await Log(LogLevel.Error, $"Image generation failed: {ex.Message}");
            return new { Success = false, Message = $"Image generation failed: {ex.Message}" };
        }
    }

    private static string SaveImage(ServiceConfig config, string outputFileName, string base64Data, string mimeType)
    {
        var extension = mimeType switch
        {
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => ".png"
        };

        var fileName = !string.IsNullOrWhiteSpace(outputFileName)
            ? outputFileName + extension
            : $"generated_{DateTime.UtcNow:yyyyMMdd_HHmmss}{extension}";

        var directory = !string.IsNullOrWhiteSpace(config.OutputDirectory)
            ? config.OutputDirectory
            : Path.GetTempPath();

        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, fileName);

        var imageBytes = Convert.FromBase64String(base64Data);
        File.WriteAllBytes(filePath, imageBytes);

        return filePath;
    }
}
