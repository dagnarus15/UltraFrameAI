using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UltraFrameAI.Resources;
using Xunit;

namespace UltraFrameAI.Tests;

public sealed class LocalizedStringsTests
{
    private static readonly string ProjectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static readonly string AppRoot = Path.Combine(ProjectRoot, "UltraFrameAI");
    private static readonly string BaseResourcePath = Path.Combine(AppRoot, "Resources", "Strings.resx");
    private static readonly string RussianResourcePath = Path.Combine(AppRoot, "Resources", "Strings.ru.resx");
    private static readonly string GermanResourcePath = Path.Combine(AppRoot, "Resources", "Strings.de.resx");

    [Fact]
    public void SetLanguage_ChangesValuesAndRaisesEvent()
    {
        var original = LocalizedStrings.CurrentLanguage;
        var raised = 0;

        void Handler(object? sender, EventArgs e) => raised++;

        LocalizedStrings.LanguageChanged += Handler;
        try
        {
            var first = original == UiLanguage.English ? UiLanguage.Russian : UiLanguage.English;
            var second = first == UiLanguage.Russian ? UiLanguage.German : UiLanguage.Russian;

            LocalizedStrings.SetLanguage(first);
            var firstValue = LocalizedStrings.QueueStatusCompleted;

            LocalizedStrings.SetLanguage(second);
            var secondValue = LocalizedStrings.QueueStatusCompleted;

            Assert.NotEqual(firstValue, secondValue);
            Assert.True(raised >= 2);
        }
        finally
        {
            LocalizedStrings.LanguageChanged -= Handler;
            LocalizedStrings.SetLanguage(original);
        }
    }

    [Fact]
    public void ResourceFiles_HaveMatchingKeySets()
    {
        var baseKeys = LoadResxKeys(BaseResourcePath);
        var russianKeys = LoadResxKeys(RussianResourcePath);
        var germanKeys = LoadResxKeys(GermanResourcePath);

        AssertSetMatches(baseKeys, russianKeys, "ru");
        AssertSetMatches(baseKeys, germanKeys, "de");
    }

    [Fact]
    public void AllLocalizedStringProperties_ResolveForEveryLanguage()
    {
        var original = LocalizedStrings.CurrentLanguage;
        try
        {
            var properties = typeof(LocalizedStrings)
                .GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(property => property.PropertyType == typeof(string)
                    && property.GetMethod is not null
                    && property.GetMethod.GetParameters().Length == 0)
                .ToArray();

            foreach (var language in new[] { UiLanguage.English, UiLanguage.Russian, UiLanguage.German })
            {
                LocalizedStrings.SetLanguage(language);

                foreach (var property in properties)
                {
                    var value = property.GetValue(null) as string;
                    Assert.False(string.IsNullOrWhiteSpace(value), $"Property '{property.Name}' resolved to an empty value for {language}.");
                }
            }
        }
        finally
        {
            LocalizedStrings.SetLanguage(original);
        }
    }

    [Fact]
    public void LiteralLocalizationKeys_ExistInEveryResourceFile()
    {
        var baseKeys = LoadResxKeys(BaseResourcePath);
        var russianKeys = LoadResxKeys(RussianResourcePath);
        var germanKeys = LoadResxKeys(GermanResourcePath);

        var keys = FindLiteralLocalizationKeys();
        Assert.All(keys, key =>
        {
            Assert.Contains(key, baseKeys);
            Assert.Contains(key, russianKeys);
            Assert.Contains(key, germanKeys);
        });
    }

    [Fact]
    public void XamlLocalizationKeys_ExistInEveryResourceFile()
    {
        var baseKeys = LoadResxKeys(BaseResourcePath);
        var russianKeys = LoadResxKeys(RussianResourcePath);
        var germanKeys = LoadResxKeys(GermanResourcePath);

        var keys = FindXamlLocalizationKeys();
        Assert.All(keys, key =>
        {
            Assert.Contains(key, baseKeys);
            Assert.Contains(key, russianKeys);
            Assert.Contains(key, germanKeys);
        });
    }

    [Fact]
    public void StringsKeysMarkdown_IsInSyncWithBaseResources()
    {
        var baseKeys = LoadResxKeys(BaseResourcePath);
        var markdownKeys = File.ReadAllLines(Path.Combine(AppRoot, "Resources", "Strings.keys.md"))
            .Where(line => line.StartsWith("- `", StringComparison.Ordinal))
            .Select(line => line[3..^1])
            .ToHashSet(StringComparer.Ordinal);

        AssertSetMatches(baseKeys, markdownKeys, "Strings.keys.md");
    }

    [Fact]
    public void StringsDesigner_IsInSyncWithBaseResources()
    {
        var baseKeys = LoadResxKeys(BaseResourcePath);
        var designerText = File.ReadAllText(Path.Combine(AppRoot, "Resources", "Strings.Designer.cs"));
        var regex = new Regex(@"public static string ([A-Za-z0-9_]+) => GetRequired\(nameof\(\1\)\);", RegexOptions.Compiled);
        var designerKeys = regex.Matches(designerText)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        AssertSetMatches(baseKeys, designerKeys, "Strings.Designer.cs");
    }

    private static HashSet<string> LoadResxKeys(string path)
    {
        var document = XDocument.Load(path);
        return document.Root?
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private static void AssertSetMatches(HashSet<string> expected, HashSet<string> actual, string language)
    {
        var missing = expected.Except(actual).OrderBy(key => key).ToArray();
        Assert.True(missing.Length == 0, $"Missing keys in {language}: {string.Join(", ", missing)}");
    }

    private static string[] FindLiteralLocalizationKeys()
    {
        var regex = new Regex("LocalizedStrings\\.Get\\(\"([^\"]+)\"\\)", RegexOptions.Compiled);
        return Directory.EnumerateFiles(AppRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => regex.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] FindXamlLocalizationKeys()
    {
        var regex = new Regex(@"\{local:Loc\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);
        return Directory.EnumerateFiles(AppRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => regex.Matches(File.ReadAllText(path)).Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }
}
