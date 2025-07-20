using System.Linq;
using System.Text;

/// <summary>
/// プロジェクト内の各ファイルの内容をMarkdown形式で生成するクラスです。
/// A class that generates the content of each file in the project in Markdown format.
/// </summary>
public class FileContentGenerator
{
    private readonly AnalyzerSettings _settings;

    /// <summary>
    /// FileContentGenerator クラスの新しいインスタンスを初期化します。
    /// Initializes a new instance of the FileContentGenerator class.
    /// </summary>
    /// <param name="settings">分析に使用する設定。/ The settings to use for the analysis.</param>
    public FileContentGenerator(AnalyzerSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// プロジェクト内のすべてのファイルに対する単一のMarkdownコンテンツを生成します。
    /// Generates a single Markdown content for all files in the project.
    /// </summary>
    /// <returns>生成されたMarkdownコンテンツ文字列。/ The generated Markdown content string.</returns>
    public string Generate()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 📄 Project Context");
        sb.AppendLine();

        GenerateRecursive(_settings.ProjectPath, sb);
        return sb.ToString();
    }

    /// <summary>
    /// 指定されたパスから再帰的にディレクトリとファイルを探索し、各ファイルに対してMarkdown生成処理を呼び出します。
    /// Recursively explores directories and files from the specified path and calls the Markdown generation process for each file.
    /// </summary>
    /// <param name="currentPath">探索を開始する現在のディレクトリパス。/ The current directory path to start exploration from.</param>
    /// <param name="sb">Markdownコンテンツを構築するためのStringBuilder。/ The StringBuilder to build the Markdown content.</param>
    private void GenerateRecursive(string currentPath, StringBuilder sb)
    {
        // Files
        foreach (var file in Directory.GetFiles(currentPath).OrderBy(f => f))
        {
            if (_settings.IgnoreList.Contains(Path.GetFileName(file))) continue;

            try
            {
                AppendMarkdownForFile(file, sb);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   [Warning] Could not process file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        // Directories
        foreach (var dir in Directory.GetDirectories(currentPath).OrderBy(d => d))
        {
            if (_settings.IgnoreList.Contains(Path.GetFileName(dir))) continue;
            GenerateRecursive(dir, sb);
        }
    }

    /// <summary>
    /// 単一のソースファイルからMarkdownコンテンツを生成し、StringBuilderに追加します。
    /// Generates Markdown content from a single source file and appends it to the StringBuilder.
    /// </summary>
    /// <param name="filePath">処理対象のソースファイルのパス。/ The path of the source file to process.</param>
    /// <param name="sb">Markdownコンテンツを構築するためのStringBuilder。/ The StringBuilder to build the Markdown content.</param>
    private void AppendMarkdownForFile(string filePath, StringBuilder sb)
    {
        string relativePath = Path.GetRelativePath(_settings.ProjectPath, filePath);

        string content = File.ReadAllText(filePath);
        string language = LanguageMapper.GetLanguage(Path.GetExtension(filePath));

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## {Path.GetFileName(filePath)}");
        sb.AppendLine();
        sb.AppendLine($"**Relative Path:** `{relativePath}`");
        sb.AppendLine();

        // 人間が閲覧しやすいように、シンタックスハイライト付きのコードブロックを<details>タグで折りたたんで表示します。
        sb.AppendLine($"**File Content:**");
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>View with syntax highlighting</summary>");
        sb.AppendLine();
        sb.AppendLine(content);
        sb.AppendLine("</details>");
        sb.AppendLine();

        sb.AppendLine($"```{language}");
        sb.AppendLine(content);
        sb.AppendLine("```");
        sb.AppendLine();
    }
}