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
    /// プロジェクト内のすべてのファイルに対するMarkdownファイルの生成を開始します。
    /// Starts the generation of Markdown files for all files in the project.
    /// </summary>
    public void Generate()
    {
        GenerateRecursive(_settings.ProjectPath);
    }

    /// <summary>
    /// 指定されたパスから再帰的にディレクトリとファイルを探索し、各ファイルに対してMarkdown生成処理を呼び出します。
    /// Recursively explores directories and files from the specified path and calls the Markdown generation process for each file.
    /// </summary>
    /// <param name="currentPath">探索を開始する現在のディレクトリパス。/ The current directory path to start exploration from.</param>
    private void GenerateRecursive(string currentPath)
    {
        // Files
        foreach (var file in Directory.GetFiles(currentPath))
        {
            if (_settings.IgnoreList.Contains(Path.GetFileName(file))) continue;

            try
            {
                CreateMarkdownForFile(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   [Warning] Could not process file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        // Directories
        foreach (var dir in Directory.GetDirectories(currentPath))
        {
            if (_settings.IgnoreList.Contains(Path.GetFileName(dir))) continue;
            GenerateRecursive(dir);
        }
    }

    /// <summary>
    /// 単一のソースファイルからMarkdownファイルを生成します。
    /// Generates a Markdown file from a single source file.
    /// </summary>
    /// <param name="filePath">処理対象のソースファイルのパス。/ The path of the source file to process.</param>
    private void CreateMarkdownForFile(string filePath)
    {
        string relativePath = Path.GetRelativePath(_settings.ProjectPath, filePath);
        string outputFilePath = Path.Combine(_settings.OutputPath, relativePath + ".md");

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));

        string content = File.ReadAllText(filePath);
        string language = LanguageMapper.GetLanguage(Path.GetExtension(filePath));

        var mdContent = new StringBuilder();
        mdContent.AppendLine($"# {Path.GetFileName(filePath)}");
        mdContent.AppendLine();
        mdContent.AppendLine($"**Relative Path:** `{relativePath}`");
        mdContent.AppendLine();
        mdContent.AppendLine($"```{language}");
        mdContent.AppendLine(content);
        mdContent.AppendLine("```");

        File.WriteAllText(outputFilePath, mdContent.ToString());
    }
}