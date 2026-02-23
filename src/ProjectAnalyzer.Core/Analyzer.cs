namespace ProjectAnalyzer.Core;
using System;
using System.IO;

/// <summary>
/// プロジェクトの分析を統括するメインクラスです。
/// The main class that orchestrates the project analysis.
/// </summary>
public class Analyzer: IDisposable
{
    private readonly AnalyzerSettings _settings;
    private readonly TreeGenerator _treeGenerator;
    private readonly FileContentGenerator _fileContentGenerator;

    /// <summary>
    /// ProjectAnalyzer クラスの新しいインスタンスを初期化します。
    /// Initializes a new instance of the ProjectAnalyzer class.
    /// </summary>
    /// <param name="settings">分析に使用する設定。/ The settings to use for the analysis.</param>
    public Analyzer(AnalyzerSettings settings)
    {
        _settings = settings;
        _treeGenerator = new TreeGenerator(settings);
        _fileContentGenerator = new FileContentGenerator(settings);
    }

    /// <summary>
    /// プロジェクトの分析処理を開始します。出力ディレクトリの準備、フォルダツリーの生成、各ファイルの内容生成を順に実行します。
    /// Starts the project analysis process. It prepares the output directory, generates the folder tree, and then generates the content for each file.
    /// </summary>
    /// <returns>生成されたテキストデータを保持するオブジェクト / An object holding the generated text data.</returns>
    public AnalyzerResult Analyze()
    {
        var result = new AnalyzerResult();

        if (_settings.OutputToFile)
        {
            PrepareOutputDirectory();
        }

        Console.WriteLine("🌳 Generating folder tree...");
        string tree = _treeGenerator.Generate();
        string treeContent = $"# 🌳 Project Folder Tree\n\n```\n{tree}\n```";
        result.ProjectTree = treeContent;

        if (_settings.OutputToFile)
        {
            File.WriteAllText(Path.Combine(_settings.OutputPath, "00_ProjectTree.md"), treeContent);
            Console.WriteLine("   -> Success: 00_ProjectTree.md\n");
        }
        else
        {
            Console.WriteLine("   -> Skipped file output (kept in memory)\n");
        }

        Console.WriteLine("📄 Generating file contents...");
        var allFilesContents = _fileContentGenerator.Generate();
        result.ProjectContexts = allFilesContents;

        if (_settings.OutputToFile)
        {
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
        else
        {
            Console.WriteLine($"   -> Skipped file output (kept {allFilesContents.Count} contexts in memory)\n");
        }

        return result;
    }

    /// <summary>
    /// 出力ディレクトリを準備します。
    /// Prepares the output directory.
    /// </summary>
    private void PrepareOutputDirectory()
    {
        if (string.IsNullOrWhiteSpace(_settings.OutputPath)) return;
        Directory.CreateDirectory(_settings.OutputPath);
    }
    
    public void Dispose()
    {
        // 一時フォルダが設定されていて、存在する場合のみ削除を実行
        if (!string.IsNullOrEmpty(_settings.TempClonePath) && Directory.Exists(_settings.TempClonePath))
        {
            try
            {
                RemoveReadOnlyAttributes(new DirectoryInfo(_settings.TempClonePath));
                Directory.Delete(_settings.TempClonePath, true);
                Console.WriteLine("🧹 Cleaned up temporary repository files.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to clean up temporary directory: {ex.Message}");
            }
        }
    }

    private void RemoveReadOnlyAttributes(DirectoryInfo directory)
    {
        foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes &= ~FileAttributes.ReadOnly;
        }
    }
}