public static class LanguageMapper
{
    /// <summary>
    /// ファイルの拡張子からMarkdownコードブロック用の言語識別子を返します。
    /// </summary>
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
            _ => "" // 不明な場合は言語指定なし
        };
    }
}