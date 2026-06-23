using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace HQ.Plugins.ImageGeneration.Models;

/// <summary>
/// Per-tool argument types — the single source of truth for both the generated LLM schema
/// (via <c>ToolSchemaGenerator</c>) and runtime binding. Property names are camel-cased for the
/// LLM. <c>[Required]</c> marks fields the model must supply.
/// </summary>

public class GenerateImageArgs
{
    [Required, Description("Text prompt describing the image to generate")]
    public string Prompt { get; set; }

    [Description("Aspect ratio of the generated image, e.g. 1:1, 16:9, 9:16, 4:3, 3:4. Defaults to 1:1.")]
    public string AspectRatio { get; set; }

    [Description("Resolution of the generated image: 512px, 1K, 2K, 4K. Defaults to 1K.")]
    public string Resolution { get; set; }

    [Description("Optional filename for the saved image (without extension). A .png extension will be added automatically.")]
    public string OutputFileName { get; set; }
}

public class DescribeImageArgs
{
    [Required, Description("Base64-encoded image to describe")]
    public string ReferenceImage { get; set; }

    [Description("Optional prompt to guide the description, e.g. 'describe the colors' or 'what text is visible'. Defaults to a general description.")]
    public string Prompt { get; set; }
}

public class EditImageArgs
{
    [Required, Description("Text prompt describing how to edit or transform the image")]
    public string Prompt { get; set; }

    [Required, Description("Base64-encoded reference image to edit")]
    public string ReferenceImage { get; set; }

    [Description("Aspect ratio of the generated image, e.g. 1:1, 16:9, 9:16, 4:3, 3:4. Defaults to 1:1.")]
    public string AspectRatio { get; set; }

    [Description("Resolution of the generated image: 512px, 1K, 2K, 4K. Defaults to 1K.")]
    public string Resolution { get; set; }

    [Description("Optional filename for the saved image (without extension). A .png extension will be added automatically.")]
    public string OutputFileName { get; set; }
}
