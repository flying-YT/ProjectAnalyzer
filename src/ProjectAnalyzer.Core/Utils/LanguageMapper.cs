namespace ProjectAnalyzer.Core.Utils;
/// <summary>
/// ファイル拡張子とMarkdownの言語識別子をマッピングするクラスです。
/// A class that maps file extensions to Markdown language identifiers.
/// </summary>
public static class LanguageMapper
{
    /// <summary>
    /// 指定された拡張子に対応する言語識別子を取得します。
    /// Gets the language identifier corresponding to the specified extension.
    /// </summary>
    /// <param name="extension">ファイル拡張子 / The file extension.</param>
    /// <returns>Markdown言語識別子 / The Markdown language identifier.</returns>
    public static string GetLanguage(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".vb" => "vb",
            ".fs" => "fsharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".html" => "html",
            ".css" => "css",
            ".json" => "json",
            ".xml" => "xml",
            ".sql" => "sql",
            ".py" => "python",
            ".java" => "java",
            ".cpp" => "cpp",
            ".c" => "c",
            ".h" => "c",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".php" => "php",
            ".md" => "markdown",
            ".sh" => "bash",
            ".ps1" => "powershell",
            _ => "" // 不明な場合は言語指定なし / No language specified if unknown
        };
    }
}