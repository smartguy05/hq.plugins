using System;
using System.IO;
using HQ.Plugins.ImageGeneration;
using HQ.Plugins.ImageGeneration.Models;
using Xunit;

namespace HQ.Plugins.Tests.ImageGeneration;

/// <summary>
/// WP6A-9: `outputFileName` (model-controlled, via generate_image/edit_image) was combined
/// with the configured output directory with no sanitization, so an absolute path or a
/// "../" traversal segment survived into Path.Combine + File.WriteAllBytes — a write-where
/// primitive with model-generated image bytes as the payload. These tests exercise the
/// internal SaveImage helper directly (InternalsVisibleTo HQ.Plugins.Tests) so the fix can be
/// pinned without a real Gemini API call.
/// </summary>
public class ImageGenerationCommandTests
{
    private static readonly string SampleBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 });

    [Fact]
    public void SaveImage_AbsoluteOutputFileName_IsContainedInConfiguredDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory("hq-imagegen-test-").FullName;
        try
        {
            var config = new ServiceConfig { OutputDirectory = tempDir };

            // Attacker-supplied absolute path pointing outside the configured output directory.
            var maliciousName = Path.Combine(Path.GetTempPath(), "hq-imagegen-escape-target", "Cookies");

            var filePath = ImageGenerationCommand.SaveImage(config, maliciousName, SampleBase64, "image/png");

            var resolvedDir = Path.GetFullPath(tempDir);
            var resolvedFile = Path.GetFullPath(filePath);
            Assert.StartsWith(resolvedDir + Path.DirectorySeparatorChar, resolvedFile);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SaveImage_TraversalOutputFileName_IsContainedInConfiguredDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory("hq-imagegen-test-").FullName;
        try
        {
            var config = new ServiceConfig { OutputDirectory = tempDir };

            var maliciousName = "../../../../etc/passwd-clobber";

            var filePath = ImageGenerationCommand.SaveImage(config, maliciousName, SampleBase64, "image/png");

            var resolvedDir = Path.GetFullPath(tempDir);
            var resolvedFile = Path.GetFullPath(filePath);
            Assert.StartsWith(resolvedDir + Path.DirectorySeparatorChar, resolvedFile);
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SaveImage_NormalFileName_StillWritesInsideConfiguredDirectory()
    {
        var tempDir = Directory.CreateTempSubdirectory("hq-imagegen-test-").FullName;
        try
        {
            var config = new ServiceConfig { OutputDirectory = tempDir };

            var filePath = ImageGenerationCommand.SaveImage(config, "my-image", SampleBase64, "image/png");

            Assert.Equal(Path.Combine(Path.GetFullPath(tempDir), "my-image.png"), Path.GetFullPath(filePath));
            Assert.True(File.Exists(filePath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
