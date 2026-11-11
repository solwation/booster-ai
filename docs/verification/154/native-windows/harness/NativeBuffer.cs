using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using SkiaSharp;

internal static class NativeBuffer
{
    public static async Task Verify(TopLevel popup, Border surface, string root, bool software)
    {
        var origin = surface.TranslatePoint(default, popup)!.Value;
        var scale = popup.RenderScaling;
        var expected = ((ISolidColorBrush)surface.Background!).Color;
        // Sample a padding strip under the top edge, clear of text and rounded corners.
        var x0 = (int)((origin.X + 30) * scale); var y0 = (int)((origin.Y + 4) * scale);
        object Sample(SKBitmap frame) {
            var pixels = new List<SKColor>();
            for (var y=y0; y<y0+3; y++) for (var x=x0; x<x0+100; x++) pixels.Add(frame.GetPixel(x,y));
            return new { sampleCount = pixels.Count, minAlpha = pixels.Min(c=>c.Alpha), maxAlpha = pixels.Max(c=>c.Alpha),
                matchingExpected = pixels.Count(c=>c.Alpha==255 && Math.Abs(c.Red-expected.R)<=1 && Math.Abs(c.Green-expected.G)<=1 && Math.Abs(c.Blue-expected.B)<=1),
                firstPixel = pixels[0].ToString() };
        }
        SKBitmap Copy()
        {
            if (software) return NativeFramebuffer.Copy(popup);
            var path = Path.Combine(root, "capture-current.png");
            if (!Program.Capture(popup.TryGetPlatformHandle()!.Handle, path)) throw new InvalidOperationException("Native PrintWindow failed");
            return SKBitmap.Decode(path);
        }
        using var positive = Copy();
        var before = JsonSerializer.SerializeToElement(Sample(positive));
        using (var data=positive.Encode(SKEncodedImageFormat.Png,100)) using(var file=File.Create(Path.Combine(root,software ? "native-framebuffer.png" : "native-gpu-capture.png"))) data.SaveTo(file);
        surface.Opacity = .5;
        await Task.Delay(700);
        using var negative = Copy();
        var half = JsonSerializer.SerializeToElement(Sample(negative));
        using (var data=negative.Encode(SKEncodedImageFormat.Png,100)) using(var file=File.Create(Path.Combine(root,"negative-half-opacity.png"))) data.SaveTo(file);
        surface.Opacity = 1;
        await Task.Delay(700);
        using var restored = Copy();
        var after = JsonSerializer.SerializeToElement(Sample(restored));
        var passed = before.GetProperty("matchingExpected").GetInt32()==300 && after.GetProperty("matchingExpected").GetInt32()==300 && half.GetProperty("matchingExpected").GetInt32()<300;
        File.WriteAllText(Path.Combine(root,"opacity.json"), JsonSerializer.Serialize(new { software, capture = software ? "native Win32 framebuffer BGRA premultiplied" : "PrintWindow of native WinUI composition HWND", expected = expected.ToString(), before, negativeControl = half, restored = after, passed },new JsonSerializerOptions { WriteIndented=true }));
        if (!passed) throw new InvalidOperationException("Native opacity/negative control did not pass");
    }
}
