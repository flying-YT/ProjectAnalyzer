/// <summary>
/// プロジェクトの分析を統括するメインクラスです。
/// The main class that orchestrates the project analysis.
/// </summary>
public class ProjectAnalyzer
{
    private readonly AnalyzerSettings _settings;
    private readonly TreeGenerator _treeGenerator;
    private readonly FileContentGenerator _fileContentGenerator;

    /// <summary>
    /// ProjectAnalyzer クラスの新しいインスタンスを初期化します。
    /// Initializes a new instance of the ProjectAnalyzer class.
    /// </summary>
    /// <param name="settings">分析に使用する設定。/ The settings to use for the analysis.</param>
    public ProjectAnalyzer(AnalyzerSettings settings)
    {
        _settings = settings;
        _treeGenerator = new TreeGenerator(settings);
        _fileContentGenerator = new FileContentGenerator(settings);
    }

    /// <summary>
    /// プロジェクトの分析処理を開始します。出力ディレクトリの準備、フォルダツリーの生成、各ファイルの内容生成を順に実行します。
    /// Starts the project analysis process. It prepares the output directory, generates the folder tree, and then generates the content for each file.
    /// </summary>
    public void Analyze()
    {
        PrepareOutputDirectory();

        Console.WriteLine("🌳 Generating folder tree...");
        string tree = _treeGenerator.Generate();
        File.WriteAllText(Path.Combine(_settings.OutputPath, "00_ProjectTree.md"), $"# 🌳 Project Folder Tree\n\n```\n{tree}\n```");
        Console.WriteLine("   -> Success: 00_ProjectTree.md\n");

        Console.WriteLine("📄 Generating file contents...");
        var allFilesContents = _fileContentGenerator.Generate();

        if (allFilesContents.Count == 1)
        {
            string outputFilePath = Path.Combine(_settings.OutputPath, "01_ProjectContext.md");
            File.WriteAllText(outputFilePath, allFilesContents[0]);
            Console.WriteLine($"   -> Success: {Path.GetFileName(outputFilePath)}\n");
        }
        else
        {
            for (int i = 0; i < allFilesContents.Count; i++)
            {
                string outputFilePath = Path.Combine(_settings.OutputPath, $"01_ProjectContext_{i + 1}.md");
                File.WriteAllText(outputFilePath, allFilesContents[i]);
                Console.WriteLine($"   -> Success: {Path.GetFileName(outputFilePath)}");
            }
            Console.WriteLine();
        }
    }

    /// <summary>
    /// 出力ディレクトリを準備します。
    /// Prepares the output directory.
    /// </summary>
    private void PrepareOutputDirectory()
    {
        // ディレクトリのクリーンは意図しない削除をする可能性があるため廃止。
        // Directory clean has been deprecated as it could cause unintended deletions.

        //if (Directory.Exists(_settings.OutputPath))
        //{
        //    Directory.Delete(_settings.OutputPath, true);
        //}
        Directory.CreateDirectory(_settings.OutputPath);
    }
}