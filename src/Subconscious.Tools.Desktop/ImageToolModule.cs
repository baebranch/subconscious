using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using System.ComponentModel;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// Image processing tool module. Provides basic image operations.
/// Port of Python's <c>desktop_tools/images.py</c>.
/// </summary>
public sealed class ImageToolModule : IToolModule
{
    public string Slug => "images";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                GetImageInfo,
                "get_image_info",
                "Get information about an image file including dimensions and format."),

            AIFunctionFactory.Create(
                ResizeImage,
                "resize_image",
                "Resize an image to specified dimensions.")
        ];
    }

    private static string GetImageInfo(
        [Description("Path to the image file.")] string path,
        EngineContext context)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(path);

            return $"""
                File: {path}
                Format: {image.RawFormat?.ToString() ?? "Unknown"}
                Dimensions: {image.Width} x {image.Height} pixels
                Horizontal DPI: {image.HorizontalResolution:F2}
                Vertical DPI: {image.VerticalResolution:F2}
                """;
        }
        catch (Exception ex)
        {
            return $"Error reading image '{path}': {ex.Message}";
        }
    }

    private static string ResizeImage(
        [Description("Path to the source image.")] string sourcePath,
        [Description("Target width in pixels.")] int width,
        [Description("Target height in pixels.")] int height,
        EngineContext context)
    {
        try
        {
            using var image = System.Drawing.Image.FromFile(sourcePath);
            using var resized = new System.Drawing.Bitmap(image, width, height);

            var format = image.RawFormat;

            // Save with same format
            var directory = Path.GetDirectoryName(sourcePath);
            var filename = Path.GetFileNameWithoutExtension(sourcePath);
            var extension = Path.GetExtension(sourcePath);
            var outputPath = Path.Combine(directory ?? ".", $"{filename}_resized{extension}");

            resized.Save(outputPath, format);

            return $"Successfully resized '{sourcePath}' to {width}x{height} and saved to '{outputPath}'";
        }
        catch (Exception ex)
        {
            return $"Error resizing image: {ex.Message}";
        }
    }
}
