using System.Linq;
using System.Text;

/// <summary>
/// プロジェクト内の各ファイルの内容をMarkdown形式で生成するクラスです。
/// A class that generates the content of each file in the project in Markdown format.
/// </summary>
public class FileContentGenerator
{
    private readonly AnalyzerSettings _settings;
    private const long MaxFileSize = 4 * 1024 * 1024; // 8MB

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
    /// プロジェクト内のすべてのファイルに対するMarkdownコンテンツのリストを生成します。
    /// Generates a list of Markdown content for all files in the project.
    /// </summary>
    /// <returns>生成されたMarkdownコンテンツ文字列のリスト。/ The list of generated Markdown content strings.</returns>
    public List<string> Generate()
    {
        var fileContents = new List<string>();
        var sb = new StringBuilder();
        sb.AppendLine("# 📄 Project Context");
        sb.AppendLine();

        long currentSize = 0;
        var allFiles = GetAllFiles(_settings.ProjectPath);

        foreach (var file in allFiles)
        {
            string fileMarkdown = GenerateMarkdownForFile(file);
            long fileSize = Encoding.UTF8.GetByteCount(fileMarkdown);

            if (currentSize + fileSize > MaxFileSize && sb.Length > 0)
            {
                fileContents.Add(sb.ToString());
                sb.Clear();
                sb.AppendLine("# 📄 Project Context (続き)");
                sb.AppendLine();
                currentSize = 0;
            }

            sb.Append(fileMarkdown);
            currentSize += fileSize;
        }

        if (sb.Length > 0)
        {
            fileContents.Add(sb.ToString());
        }

        return fileContents;
    }

    private List<string> GetAllFiles(string path)
    {
        var files = new List<string>();

        foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
        {
            if (!_settings.IgnoreList.Contains(Path.GetFileName(file)))
            {
                files.Add(file);
            }
        }

        foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
        {
            if (!_settings.IgnoreList.Contains(Path.GetFileName(dir)))
            {
                files.AddRange(GetAllFiles(dir));
            }
        }

        return files;
    }

    /// <summary>
    /// 単一のソースファイルからMarkdownコンテンツを生成します。
    /// Generates Markdown content from a single source file.
    /// </summary>
    /// <param name="filePath">処理対象のソースファイルのパス。/ The path of the source file to process.</param>
    /// <returns>生成されたMarkdownコンテンツ。/ The generated Markdown content.</returns>
    private string GenerateMarkdownForFile(string filePath)
    {
        try
        {
            var sb = new StringBuilder();
            string relativePath = Path.GetRelativePath(_settings.ProjectPath, filePath);
            string content = File.ReadAllText(filePath);
            string language = LanguageMapper.GetLanguage(Path.GetExtension(filePath));

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## {Path.GetFileName(filePath)}");
            sb.AppendLine();
            sb.AppendLine($"**Relative Path:** `{relativePath}`");
            sb.AppendLine();

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

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [Warning] Could not process file '{Path.GetFileName(filePath)}': {ex.Message}");
            return string.Empty;
        }
    }
}