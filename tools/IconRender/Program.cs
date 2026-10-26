using SkiaSharp;
using Svg.Skia;

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
Console.WriteLine($"Wrote boosterai.png and boosterai.ico ({string.Join(", ", sizes)}) in {assets}");
