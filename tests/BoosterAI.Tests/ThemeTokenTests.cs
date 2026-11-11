using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>The colour, surface and radius tokens in App.axaml (#147): colours live only there, the token pairs
/// the UI uses reach WCAG contrast, and corner radii stay on the 4/6/8 scale.</summary>
public class ThemeTokenTests
{
    private static readonly string Src = Path.Combine(RepoRoot(), "src", "BoosterAI");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Src, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(f => !Path.GetRelativePath(Src, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"));

    // Tab colour palettes are user-picked stripe colours, not status or error colours.
    private static readonly string[] TabColourFiles = ["Catalog.cs", "Config.cs"];

    [Fact]
    public void NoHardCodedColoursOutsideAppAxaml()
    {
        var hex = new Regex(@"#(?:[0-9A-Fa-f]{8}|[0-9A-Fa-f]{6})\b");
        var named = new Regex(@"(?:Foreground|Background|Fill|Stroke|BorderBrush)=""(?:Red|Green|Orange|Yellow|OrangeRed|Crimson|DarkRed|LimeGreen|Gold)""|Brushes\.(?:Red|Green|Orange|Yellow|OrangeRed|Crimson|DarkRed|LimeGreen|Gold)\b|Colors\.(?:Red|Green|Orange|Yellow|OrangeRed|Crimson|DarkRed|LimeGreen|Gold)\b");
        var offenders = new List<string>();
        foreach (var file in SourceFiles())
        {
            var name = Path.GetFileName(file);
            if (name == "App.axaml" || TabColourFiles.Contains(name)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (hex.IsMatch(lines[i]) || named.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(Src, file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0, "Use a theme token from App.axaml instead of:\n" + string.Join('\n', offenders));
    }

    // Circles (the numbered step circles) keep a radius of half their size; everything else is on the 4/6/8 scale.
    private static readonly string[] Circles = ["Styles.axaml: 11"];

    [Fact]
    public void CornerRadiiAreFourSixOrEight()
    {
        var xaml = new Regex(@"CornerRadius(?:""\s+Value)?=""([^""]+)""");
        var code = new Regex(@"new CornerRadius\(([^)]*)\)");
        var offenders = new List<string>();
        foreach (var file in SourceFiles().Where(f => Path.GetFileName(f) != "App.axaml"))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in xaml.Matches(text).Concat(code.Matches(text)))
            {
                var value = m.Groups[1].Value.Trim();
                if (value is "{StaticResource RadiusSmall}" or "{StaticResource RadiusMedium}" or "{StaticResource RadiusLarge}") continue;
                var ok = value.Split(',').All(p => double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r is 0 or 4 or 6 or 8);
                if (!ok && !Circles.Contains($"{Path.GetFileName(file)}: {value}"))
                    offenders.Add($"{Path.GetRelativePath(Src, file)}: {m.Value}");
            }
        }
        Assert.True(offenders.Count == 0, "CornerRadius must be 4, 6 or 8 (RadiusSmall/Medium/Large):\n" + string.Join('\n', offenders));
    }

    private static Dictionary<string, string> Theme(string variant)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(Path.Combine(Src, "App.axaml"));
        var dict = doc.Descendants().Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(x + "Key") == variant);
        return dict.Elements().Where(e => e.Name.LocalName == "SolidColorBrush")
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
    }

    private static double Luminance(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length == 8) h = h[2..];
        double Channel(int i)
        {
            var c = int.Parse(h.Substring(i, 2), NumberStyles.HexNumber) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(0) + 0.7152 * Channel(2) + 0.0722 * Channel(4);
    }

    internal static double Contrast(string a, string b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    [Theory]
    [InlineData("#FFFFFF", "#000000", 21.0)]
    [InlineData("#6B6F80", "#FFFFFF", 5.0)]
    [InlineData("#8A8EA0", "#1C1E26", 5.1)]
    [InlineData("#FFFFFF", "#C62F35", 5.4)]
    public void ContrastMatchesWcag(string a, string b, double expected) =>
        Assert.Equal(expected, Contrast(a, b), 1);

    // Text tokens on the surfaces they appear on: body text (WCAG 1.4.3, 4.5:1).
    [Theory]
    [InlineData("Light", "DangerBrush", "BackgroundBrush")]
    [InlineData("Dark", "DangerBrush", "BackgroundBrush")]
    [InlineData("Light", "DangerBrush", "SurfaceBrush")]
    [InlineData("Dark", "DangerBrush", "SurfaceBrush")]
    [InlineData("Light", "DangerBrush", "DangerSurfaceBrush")]
    [InlineData("Dark", "DangerBrush", "DangerSurfaceBrush")]
    [InlineData("Light", "SuccessBrush", "BackgroundBrush")]
    [InlineData("Dark", "SuccessBrush", "BackgroundBrush")]
    [InlineData("Light", "SuccessBrush", "SurfaceBrush")]
    [InlineData("Dark", "SuccessBrush", "SurfaceBrush")]
    [InlineData("Light", "WarningBrush", "BackgroundBrush")]
    [InlineData("Dark", "WarningBrush", "BackgroundBrush")]
    [InlineData("Light", "WarningBrush", "SurfaceBrush")]
    [InlineData("Dark", "WarningBrush", "SurfaceBrush")]
    [InlineData("Light", "InfoBrush", "BackgroundBrush")]
    [InlineData("Dark", "InfoBrush", "BackgroundBrush")]
    [InlineData("Light", "InfoBrush", "SurfaceBrush")]
    [InlineData("Dark", "InfoBrush", "SurfaceBrush")]
    [InlineData("Light", "RecommendedBrush", "BackgroundBrush")]
    [InlineData("Dark", "RecommendedBrush", "BackgroundBrush")]
    [InlineData("Light", "TextBrush", "SurfaceStrongBrush")]
    [InlineData("Dark", "TextBrush", "SurfaceStrongBrush")]
    [InlineData("Light", "TextBrush", "DangerSurfaceBrush")]
    [InlineData("Dark", "TextBrush", "DangerSurfaceBrush")]
    [InlineData("Light", "TextBrush", "WarningSurfaceBrush")]
    [InlineData("Dark", "TextBrush", "WarningSurfaceBrush")]
    [InlineData("Light", "MutedBrush", "BackgroundBrush")]
    [InlineData("Dark", "MutedBrush", "BackgroundBrush")]
    [InlineData("Light", "MutedBrush", "SurfaceBrush")]
    [InlineData("Dark", "MutedBrush", "SurfaceBrush")]
    [InlineData("Light", "MutedBrush", "SurfaceStrongBrush")]
    [InlineData("Dark", "MutedBrush", "SurfaceStrongBrush")]
    [InlineData("Light", "FaintBrush", "BackgroundBrush")]
    [InlineData("Dark", "FaintBrush", "BackgroundBrush")]
    [InlineData("Light", "FaintBrush", "SurfaceBrush")]
    [InlineData("Dark", "FaintBrush", "SurfaceBrush")]
    [InlineData("Light", "LinkBrush", "SurfaceBrush")]
    [InlineData("Dark", "LinkBrush", "SurfaceBrush")]
    public void TextTokensReachAa(string variant, string text, string surface)
    {
        var t = Theme(variant);
        var ratio = Contrast(t[text], t[surface]);
        Assert.True(ratio >= 4.5, $"{variant} {text} {t[text]} on {surface} {t[surface]}: {ratio:0.00}:1");
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void WhiteTextOnDangerFillReachesAa(string variant) =>
        Assert.True(Contrast("#FFFFFF", Theme(variant)["DangerFillBrush"]) >= 4.5);

    // Field edges are user-interface components (WCAG 1.4.11, 3:1), also inside cards.
    [Theory]
    [InlineData("Light", "BackgroundBrush")]
    [InlineData("Dark", "BackgroundBrush")]
    [InlineData("Light", "SurfaceBrush")]
    [InlineData("Dark", "SurfaceBrush")]
    public void FieldBordersReachThreeToOne(string variant, string surface)
    {
        var t = Theme(variant);
        foreach (var key in new[] { "FieldBorderBrush", "TextControlBorderBrush", "ComboBoxBorderBrush" })
            Assert.True(Contrast(t[key], t[surface]) >= 3, $"{variant} {key} on {surface}: {Contrast(t[key], t[surface]):0.00}:1");
    }

    [Theory]
    [InlineData("Light", "#FFFFFF", "#F5F6F9", "#6B6F80")]
    [InlineData("Dark", "#14151B", "#1C1E26", "#8A8EA0")]
    public void TokensHaveTheAgreedValues(string variant, string background, string surface, string faint)
    {
        var t = Theme(variant);
        Assert.Equal(background, t["BackgroundBrush"]);
        Assert.Equal(surface, t["SurfaceBrush"]);
        Assert.Equal(faint, t["FaintBrush"]);
        Assert.Equal("#8A8FA0", t["NeutralStatusBrush"]);
        Assert.Equal("#C62F35", t["DangerFillBrush"]);
    }

    // The Fluent keys carry the same values as the tokens they stand for.
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void FluentKeysFollowTheTokens(string variant)
    {
        var t = Theme(variant);
        Assert.Equal(t["SurfaceStrongBrush"], t["ButtonBackground"]);
        Assert.Equal(t["BorderBrush"], t["ButtonBackgroundPointerOver"]);
        Assert.Equal(t["SurfaceBrush"], t["ButtonBackgroundPressed"]);
        Assert.Equal(t["SurfaceBrush"], t["ToolTipBackground"]);
        Assert.Equal(t["BorderBrush"], t["ToolTipBorderBrush"]);
        Assert.Equal(t["BackgroundBrush"], t["ComboBoxBackground"]);
        Assert.Equal(t["BackgroundBrush"], t["TextControlBackground"]);
        Assert.Equal(t["SurfaceBrush"], t["ExpanderHeaderBackground"]);
        Assert.Equal(t["SurfaceBrush"], t["ComboBoxDropDownBackground"]);
        Assert.Equal(t["FieldBorderBrush"], t["ComboBoxBorderBrush"]);
        Assert.Equal(t["FieldBorderBrush"], t["TextControlBorderBrush"]);
    }

    [Fact]
    public void BothThemesDefineTheSameKeys() =>
        Assert.Equal(Theme("Light").Keys.Order(), Theme("Dark").Keys.Order());

    [Fact]
    public void StatusDotsUseThemeTokens()
    {
        var keys = Theme("Light").Keys;
        foreach (var key in new[] { "InfoDotBrush", "SuccessDotBrush", "NeutralStatusBrush", "WarningDotBrush", "DangerDotBrush" })
            Assert.Contains(key, keys);
    }
}
