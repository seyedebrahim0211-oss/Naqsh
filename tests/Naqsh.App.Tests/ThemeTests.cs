using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Naqsh.App.Tests;

/// <summary>
/// Portable checks of the WPF app's resources (the WPF runtime itself is not available on Linux).
/// </summary>
public partial class ThemeTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void ColorTokens_MatchDesignDocument()
    {
        // docs/DESIGN.md table rows: | token | #HEX | use |
        var design = File.ReadAllText(Path.Combine(RepoRoot.Path, "docs", "DESIGN.md"));
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in TokenRow().Matches(design))
        {
            tokens[m.Groups[1].Value] = m.Groups[2].Value.ToUpperInvariant();
            if (m.Groups[3].Success)
            {
                tokens[m.Groups[1].Value + ".fill"] = m.Groups[3].Value.ToUpperInvariant();
            }
        }

        Assert.True(tokens.Count >= 11, "design tokens not found");
        var colors = XDocument.Load(Path.Combine(RepoRoot.Path, "src", "Naqsh.App", "Theme", "Colors.xaml"));
        var xaml = colors.Root!.Elements()
            .Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value.Trim().ToUpperInvariant(), StringComparer.Ordinal);
        foreach (var (token, hex) in tokens)
        {
            Assert.True(xaml.TryGetValue(token, out var actual), $"token {token} missing in Colors.xaml");
            Assert.Equal(hex, actual);
        }
    }

    [Fact]
    public void Fonts_AreBundled_WithOflLicenses()
    {
        var fonts = Path.Combine(RepoRoot.Path, "src", "Naqsh.App", "Assets", "Fonts");
        foreach (var f in new[] { "Vazirmatn-Regular.ttf", "Vazirmatn-Medium.ttf", "Vazirmatn-Bold.ttf", "Vazirmatn-ExtraBold.ttf", "Lalezar-Regular.ttf" })
        {
            Assert.True(new FileInfo(Path.Combine(fonts, f)).Length > 10_000, f);
        }

        Assert.Contains("SIL Open Font License", File.ReadAllText(Path.Combine(fonts, "Vazirmatn-OFL.txt")), StringComparison.Ordinal);
        Assert.Contains("SIL Open Font License", File.ReadAllText(Path.Combine(fonts, "Lalezar-OFL.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public void AllXaml_IsWellFormed()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src", "Naqsh.App"), "*.xaml", SearchOption.AllDirectories))
        {
            var doc = XDocument.Load(file);
            Assert.NotNull(doc.Root);
        }
    }

    [Fact]
    public void MonitorRow_IsAlwaysLeftToRight()
    {
        // docs/DESIGN.md: the monitor row (Preview | transitions | Program) never mirrors.
        var main = XDocument.Load(Path.Combine(RepoRoot.Path, "src", "Naqsh.App", "Views", "MainWindow.xaml"), LoadOptions.None);
        var hosts = main.Descendants().Where(e => e.Name.LocalName == "VideoMonitorHost").ToList();
        Assert.Equal(2, hosts.Count);
        foreach (var host in hosts)
        {
            var row = host.Ancestors().First(a => a.Name.LocalName == "Grid" && (string?)a.Attribute("FlowDirection") is not null);
            Assert.Equal("LeftToRight", (string?)row.Attribute("FlowDirection"));
        }
    }

    [GeneratedRegex(@"^\|\s*([a-z]+\.[a-z]+|border)\s*\|\s*(#[0-9A-Fa-f]{6})(?:\s*\(fill\s*(#[0-9A-Fa-f]{6})\))?\s*\|", RegexOptions.Multiline)]
    private static partial Regex TokenRow();
}

internal static class RepoRoot
{
    public static string Path { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (CLAUDE.md) not found.");
    }
}
