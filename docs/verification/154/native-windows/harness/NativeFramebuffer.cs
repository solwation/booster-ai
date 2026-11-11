using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using SkiaSharp;

// Scratch verification only; reflection is pinned to Avalonia 11.3.22.
internal static class NativeFramebuffer
{
    private static object? Member(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            if (type.GetField(name, flags) is { } field) return field.GetValue(target);
            if (type.GetProperty(name, flags) is { } property) return property.GetValue(target);
        }
        return null;
    }

    private static object Impl(TopLevel top) =>
        Member(top, "PlatformImpl") ?? throw new InvalidOperationException("No native platform implementation");

    public static SKBitmap Copy(TopLevel top)
    {
        var impl = Impl(top);
        if (impl.GetType().Assembly.GetName().Name != "Avalonia.Win32")
            throw new InvalidOperationException("This is not an Avalonia Win32 window");
        var framebuffer = Member(impl, "_framebuffer") ?? throw new InvalidOperationException("No Win32 framebuffer manager");
        var gate = Member(framebuffer, "_lock") ?? throw new InvalidOperationException("No framebuffer lock");
        lock (gate)
        {
            // Reflection boxes Nullable<FramebufferData> as its value, or null before software rendering.
            var data = Member(framebuffer, "_framebufferData") ??
                throw new InvalidOperationException("No software frame rendered: GPU mode may be active");
            var size = (PixelSize)(Member(data, "Size") ?? throw new InvalidOperationException("No framebuffer size"));
            var rowBytes = (int)(Member(data, "RowBytes") ?? throw new InvalidOperationException("No framebuffer stride"));
            var blob = Member(data, "Data") ?? throw new InvalidOperationException("No framebuffer storage");
            var address = (IntPtr)(Member(blob, "Address") ?? throw new InvalidOperationException("No framebuffer address"));
            if (size.Width <= 0 || size.Height <= 0 || address == IntPtr.Zero)
                throw new InvalidOperationException("Invalid framebuffer");
            var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            try
            {
                var row = new byte[checked(size.Width * 4)];
                for (var y = 0; y < size.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(address, checked(y * rowBytes)), row, 0, row.Length);
                    Marshal.Copy(row, 0, IntPtr.Add(bitmap.GetPixels(), checked(y * bitmap.RowBytes)), row.Length);
                }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
    }

    public static object Describe(TopLevel top)
    {
        var impl = Impl(top);
        var framebuffer = Member(impl, "_framebuffer");
        var glSurface = Member(impl, "_glSurface");
        return new
        {
            PlatformImplementation = impl.GetType().FullName,
            NativeHandle = top.TryGetPlatformHandle()?.Handle.ToInt64(),
            top.RenderScaling,
            ActualTransparency = top.ActualTransparencyLevel.ToString(),
            UseRedirectionBitmap = Member(impl, "UseRedirectionBitmap"),
            GraphicsSurface = glSurface?.GetType().FullName,
            CompositionEffectsSurface = Member(impl, "CompositionEffectsSurface")?.GetType().FullName,
            SoftwareFramePresent = framebuffer is not null && Member(framebuffer, "_framebufferData") is not null,
            Renderer = Member(top, "Renderer")?.GetType().FullName,
        };
    }
}
