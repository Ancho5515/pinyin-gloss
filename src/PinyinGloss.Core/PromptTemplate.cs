using System.Text.RegularExpressions;

namespace PinyinGloss.Core;
public static class LanguageCatalog
{
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English", ["ja"] = "Japanese", ["ko"] = "Korean", ["fr"] = "French", ["de"] = "German",
        ["es"] = "Spanish", ["ru"] = "Russian", ["ar"] = "Arabic", ["pt"] = "Portuguese", ["it"] = "Italian",
        ["zh"] = "Chinese", ["vi"] = "Vietnamese", ["th"] = "Thai", ["id"] = "Indonesian", ["hi"] = "Hindi",
        ["nl"] = "Dutch", ["pl"] = "Polish", ["tr"] = "Turkish", ["uk"] = "Ukrainian", ["sv"] = "Swedish"
    };
    public static string Name(string code) => Names.TryGetValue(code, out var name) ? name :
        System.Globalization.CultureInfo.GetCultureInfo(code).EnglishName;
}
public static partial class PromptTemplate
{
    public const string Default = "Translate this {{source_language_name}} word or short phrase into {{target_language_name}}.\n{{source_language_name}}: {{candidate}}\n{{target_language_name}}:";
    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}")]
    private static partial Regex VariableRegex();
    public static string Render(string template, string candidate, string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 8192) throw new ArgumentException("提示词必须为 1–8192 个字符");
        return VariableRegex().Replace(template, m => m.Groups[1].Value.Trim() switch
        {
            "candidate" => candidate, "source_language_name" => "Chinese", "target_language_name" => LanguageCatalog.Name(targetLanguage),
            _ => throw new ArgumentException("未知提示词变量: " + m.Groups[1].Value)
        });
    }
}
