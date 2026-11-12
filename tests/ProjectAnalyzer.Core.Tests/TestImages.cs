using SkiaSharp;

namespace ProjectAnalyzer.Core.Tests;

/// <summary>
/// OCRのテストに使う画像を生成するヘルパーです。
/// A helper that generates images for the OCR tests.
/// </summary>
internal static class TestImages
{
    /// <summary>
    /// 指定した文字列を白地に黒で描画したPNGを生成します。
    /// 日本語フォントの有無に左右されないよう、英数字の文字列で使用してください。
    /// Renders the given text in black on white and returns it as PNG bytes.
    /// Use alphanumeric text so that the result does not depend on Japanese fonts being installed.
    /// </summary>
    public static byte[] RenderTextPng(string text)
    {
        using var font = new SKFont(SKTypeface.Default, 32);
        int width = (int)font.MeasureText(text) + 80;
        using var bitmap = new SKBitmap(width, 100);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            canvas.Clear(SKColors.White);
            canvas.DrawText(text, 40, 62, font, paint);
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
