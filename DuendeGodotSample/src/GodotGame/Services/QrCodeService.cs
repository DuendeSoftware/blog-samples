using Godot;
using QRCoder;

namespace GodotGame.Services;

/// <summary>
/// Generates QR code images as Godot ImageTextures using QRCoder.
/// </summary>
public static class QrCodeService
{
    /// <summary>
    /// Generates a QR code for the given text and returns it as a Godot <see cref="ImageTexture"/>.
    /// </summary>
    /// <param name="text">The URL or text to encode.</param>
    /// <param name="pixelsPerModule">Size of each QR module in pixels (default 10).</param>
    /// <returns>An ImageTexture containing the QR code, or null on failure.</returns>
    public static ImageTexture? GenerateQrTexture(string text, int pixelsPerModule = 10)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var qrCodeData = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);

            // PngByteQRCode produces actual PNG bytes (not BMP)
            var pngQr = new PngByteQRCode(qrCodeData);
            var pngBytes = pngQr.GetGraphic(pixelsPerModule);

            // Load the PNG bytes into a Godot Image
            var image = new Image();
            var error = image.LoadPngFromBuffer(pngBytes);
            if (error != Error.Ok)
            {
                Console.Error.WriteLine($"[QrCodeService] Failed to load PNG into Image: {error}");
                return null;
            }

            // Convert to ImageTexture for display in a TextureRect
            return ImageTexture.CreateFromImage(image);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[QrCodeService] QR generation error: {ex.Message}");
            return null;
        }
    }
}
