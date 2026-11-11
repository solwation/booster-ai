using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace BoosterAI.Tests;

/// <summary>The type scale (#149): display 28/34, title 20/26, subtitle 16/22, sectionhead 14/20, body 14/20,
/// body-small 13/18, caption 12/16; no local font size outside it and no Bold weight (only Regular, Medium, SemiBold).</summary>
public class TypeScaleTests
{
    private static readonly double[] Sizes = [12, 13, 14, 16, 20, 28];
    private static readonly string Src = Path.Combine(RepoRoot(), "src", "BoosterAI");

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BoosterAI.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> Files(string ext) =>
        Directory.EnumerateFiles(Src, "*." + ext, SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(Src, f).Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj"));

    [Fact]
    public void NoFontSizeOutsideTheScale()
    {
        var offenders = new List<string>();
        var xaml = new Regex(@"FontSize=""([^""]*)""|Property=""FontSize""\s+Value=""([^""]*)""");
        foreach (var f in Files("axaml"))
            foreach (Match m in xaml.Matches(File.ReadAllText(f)))
            {
                var v = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || !Sizes.Contains(d))
                    offenders.Add($"{Path.GetFileName(f)}: {v}");
            }
        var code = new Regex(@"\bFontSize\s*=\s*([0-9.]+|[A-Za-z_]+)\b|FontSizeValue\s*=\s*([0-9.]+)");
        foreach (var f in Files("cs"))
            foreach (Match m in code.Matches(File.ReadAllText(f)))
            {
                var v = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value;
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !Sizes.Contains(d))
                    offenders.Add($"{Path.GetFileName(f)}: {v}");
            }
        Assert.Empty(offenders);
    }

    [Fact]
    public void NoBoldWeight()
    {
        var offenders = Files("axaml").Concat(Files("cs"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"FontWeight=""(Bold|ExtraBold|Black)""|FontWeight\.(Bold|ExtraBold|Black)\b|Value=""Bold"""))
            .Select(Path.GetFileName).ToList();
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("display", "28", "34", "SemiBold")]
    [InlineData("title", "20", "26", "SemiBold")]
    [InlineData("subtitle", "16", "22", "SemiBold")]
    [InlineData("sectionhead", "14", "20", "SemiBold")]
    [InlineData("body", "14", "20", null)]
    [InlineData("body-small", "13", "18", null)]
    [InlineData("caption", "12", "16", null)]
    public void ScaleClassesHaveTheAgreedSizes(string cls, string size, string line, string? weight)
    {
        XNamespace ns = "https://github.com/avaloniaui";
        var style = XDocument.Load(Path.Combine(Src, "Styles.axaml")).Descendants(ns + "Style")
            .Single(s => (string?)s.Attribute("Selector") == "TextBlock." + cls);
        string? Get(string p) => style.Elements(ns + "Setter").FirstOrDefault(e => (string?)e.Attribute("Property") == p)?.Attribute("Value")?.Value;
        Assert.Equal(size, Get("FontSize"));
        Assert.Equal(line, Get("LineHeight"));
        Assert.Equal(weight, Get("FontWeight"));
    }
}
