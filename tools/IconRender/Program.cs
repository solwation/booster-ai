using SkiaSharp;
using Svg.Skia;

// Render bundled agent marks for Avalonia's native Image control; no runtime SVG dependency.
if (args.FirstOrDefault() == "--agents")
{
    var directory = Path.GetFullPath(args[1]);
    foreach (var file in Directory.GetFiles(directory, "*.svg"))
    foreach (var theme in new[] { "light", "dark" })
    {
        var ink = theme == "dark" ? "#F4F4F5" : "#202124";
        var source = File.ReadAllText(file).Replace("currentColor", ink).Replace("#171717", ink).Replace("#000000", ink);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(source));
        using var svg = new SKSvg();
        var picture = svg.Load(stream) ?? throw new InvalidOperationException(file);
        using var bitmap = new SKBitmap(new SKImageInfo(128, 128, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        var scale = 128 / Math.Max(picture.CullRect.Width, picture.CullRect.Height);
        canvas.Translate((128 - picture.CullRect.Width * scale) / 2, (128 - picture.CullRect.Height * scale) / 2);
        canvas.Scale(scale);
        canvas.DrawPicture(picture);
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        File.WriteAllBytes(Path.Combine(directory, Path.GetFileNameWithoutExtension(file) + "-" + theme + ".png"), image.Encode(SKEncodedImageFormat.Png, 100).ToArray());
    }
    Console.WriteLine("Rendered agent marks at 128px for light and dark themes.");
    return;
}

// Windows picks the nearest ICO size and scales it, so ship every size the shell uses.
int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
const int SmallUpTo = 24; // below this "ai" is unreadable; use the bolt-only variant

var assets = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../assets"));
if (args.Length > 0) assets = Path.GetFullPath(args[0]);

byte[] Render(string svgFile, int size)
{
    using var svg = new SKSvg();
    var picture = svg.Load(Path.Combine(assets, svgFile)) ?? throw new InvalidOperationException(svgFile);
    using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Unpremul));
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(size / picture.CullRect.Width, size / picture.CullRect.Height);
    canvas.DrawPicture(picture);
    canvas.Flush();
    using var image = SKImage.FromBitmap(bitmap);
    return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
}

var images = sizes.Select(s => (Size: s, Png: Render(s <= SmallUpTo ? "boosterai-small.svg" : "boosterai.svg", s))).ToList();
File.WriteAllBytes(Path.Combine(assets, "boosterai.png"), images.Single(i => i.Size == 256).Png);
// Bolt-only mark for in-app images of 32 px or less (the setup wizard header); 64 px stays sharp at 200 % scaling.
File.WriteAllBytes(Path.Combine(assets, "boosterai-small.png"), Render("boosterai-small.svg", 64));

// ICO: header, one directory entry per image, then the PNG payloads.
using var ico = new BinaryWriter(File.Create(Path.Combine(assets, "boosterai.ico")));
ico.Write((ushort)0); ico.Write((ushort)1); ico.Write((ushort)images.Count);
var offset = 6 + 16 * images.Count;
foreach (var (size, png) in images)
{
    ico.Write((byte)(size % 256)); ico.Write((byte)(size % 256)); ico.Write((byte)0); ico.Write((byte)0);
    ico.Write((ushort)1); ico.Write((ushort)32); ico.Write(png.Length); ico.Write(offset);
    offset += png.Length;
}
foreach (var (_, png) in images) ico.Write(png);
Console.WriteLine($"Wrote boosterai.png, boosterai-small.png and boosterai.ico ({string.Join(", ", sizes)}) in {assets}");
