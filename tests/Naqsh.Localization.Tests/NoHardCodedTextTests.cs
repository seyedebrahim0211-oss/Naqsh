using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Naqsh.Localization.Tests;

/// <summary>
/// CLAUDE.md rule 8: every user-visible string comes from resources. These tests scan the UI
/// sources: XAML text attributes must be bindings (e.g. {loc:Loc Key}), and C# code must not
/// assign literal text to UI properties or contain Persian/Arabic literals.
/// </summary>
public partial class NoHardCodedTextTests
{
    private static readonly string[] TextAttributes = ["Text", "Content", "Header", "ToolTip", "Title", "Description"];

    [Fact]
    public void Xaml_HasNoLiteralText()
    {
        var problems = new List<string>();
        var files = Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.xaml", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var el in doc.Descendants())
            {
                foreach (var attr in el.Attributes())
                {
                    var name = attr.Name.LocalName;
                    if (!TextAttributes.Contains(name) || attr.Value.StartsWith('{'))
                    {
                        continue;
                    }

                    if (HasLetters(attr.Value))
                    {
                        problems.Add($"{Path.GetFileName(file)}:{((System.Xml.IXmlLineInfo)el).LineNumber} {el.Name.LocalName}.{name}=\"{attr.Value}\"");
                    }
                }

                // <TextBlock>literal</TextBlock> or <Run>literal</Run>
                if (!el.HasElements && el.Name.LocalName is "TextBlock" or "Run" or "Label" && HasLetters(el.Value))
                {
                    problems.Add($"{Path.GetFileName(file)}: element text \"{el.Value}\"");
                }
            }
        }

        Assert.True(problems.Count == 0, "Hard-coded UI text:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void CSharp_UiCode_HasNoLiteralText()
    {
        var problems = new List<string>();
        var appFolder = Path.Combine(RepoRoot.Path, "src", "Naqsh.App");
        foreach (var file in Directory.EnumerateFiles(appFolder, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (UiAssignment().IsMatch(lines[i]) || MessageBoxLiteral().IsMatch(lines[i]))
                {
                    problems.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(problems.Count == 0, "Hard-coded UI text in C#:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Sources_HaveNoPersianLiterals()
    {
        // Persian text belongs in Strings.fa-IR.resx only (tests may use Persian sample data).
        var problems = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".xaml", StringComparison.Ordinal)) ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("<!--", StringComparison.Ordinal))
                {
                    continue;
                }

                if (ArabicLiteral().IsMatch(lines[i]))
                {
                    problems.Add($"{Path.GetRelativePath(RepoRoot.Path, file)}:{i + 1}");
                }
            }
        }

        Assert.True(problems.Count == 0, "Persian/Arabic literals outside resources:\n" + string.Join('\n', problems));
    }

    [Fact]
    public void Xaml_UsesOnlyExistingResourceKeys()
    {
        var keys = LocalizationService.AllKeys().ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match m in LocKey().Matches(File.ReadAllText(file)))
            {
                if (!keys.Contains(m.Groups[1].Value))
                {
                    missing.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
                }
            }
        }

        Assert.True(missing.Count == 0, "Unknown resource keys:\n" + string.Join('\n', missing));
    }

    private static bool HasLetters(string s) => s.Any(char.IsLetter);

    [GeneratedRegex(@"\.(Text|Content|Title|Header|ToolTip)\s*=\s*\$?""[^""]*\p{L}")]
    private static partial Regex UiAssignment();

    [GeneratedRegex(@"MessageBox\.Show\([^)]*""[^""]*\p{L}")]
    private static partial Regex MessageBoxLiteral();

    // A string/char literal or XAML attribute value containing Arabic-script characters.
    [GeneratedRegex("[\"'][^\"'\\n]*[\\u0600-\\u06FF][^\"'\\n]*[\"']")]
    private static partial Regex ArabicLiteral();

    [GeneratedRegex(@"\{loc:Loc (?:Key=)?([A-Za-z0-9_]+)\}")]
    private static partial Regex LocKey();
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
