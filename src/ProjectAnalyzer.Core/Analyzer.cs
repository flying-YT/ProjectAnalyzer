using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Generators;

namespace ProjectAnalyzer.Core;
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
        string treeContent = _settings.OmitCodeBlockTicks 
            ? $"# 🌳 Project Folder Tree\n\n{tree}" 
            : $"# 🌳 Project Folder Tree\n\n```\n{tree}\n```";
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

        // 【修正】個別出力モードか統合出力モードかで分岐
        // [Fix] Branch based on individual file output mode or integrated output mode.
        if (_settings.OutputPerFile)
        {
            var individualContents = _fileContentGenerator.GeneratePerFile();
            result.IndividualFileContexts = individualContents;

            if (_settings.OutputToFile)
            {
                // 個別ファイル出力用の親ディレクトリを作成
                // Create parent directory for individual file output.
                string contextDir = Path.Combine(_settings.OutputPath, "01_ProjectContexts");
                Directory.CreateDirectory(contextDir);

                foreach (var item in individualContents)
                {
                    string outputFilePath = Path.Combine(contextDir, item.RelativePath);
                    // サブディレクトリが存在しない場合は階層構造ごと作成する（重複回避）
                    // Create directory structure if subdirectory does not exist (avoid duplication).
                    Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);
                    
                    File.WriteAllText(outputFilePath, item.Content);
                }
                Console.WriteLine($"   -> Success: Generated {individualContents.Count} individual markdown files in '01_ProjectContexts/' directory\n");
            }
            else
            {
                Console.WriteLine($"   -> Skipped file output (kept {individualContents.Count} individual contexts in memory)\n");
            }
        }
        else
        {
            // 従来の統合出力モード
            // Traditional integrated output mode.
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
        }

        // 内容を抽出できなかったファイルは、原本をそのまま出力先へコピーする。
        // PDFなどはテキスト化できないが、コピーがあればAIツールへ直接アップロードできるため。
        // Files whose content could not be extracted are copied to the output as-is.
        // A PDF cannot be turned into text, but a copy can be uploaded to an AI tool directly.
        var skippedFiles = _fileContentGenerator.SkippedBinaryFiles;
        result.SkippedFiles = skippedFiles
            .Select(f => Path.GetRelativePath(_settings.ProjectPath, f))
            .ToList();

        if (_settings.OutputToFile)
        {
            CopySkippedFiles(skippedFiles);
        }

        return result;
    }

    /// <summary>
    /// 内容を抽出できなかったファイルの原本を、元の階層構造を保ったまま出力先へコピーします。
    /// 同名ファイルの衝突や生成物との混在を避けるため、専用のサブフォルダへ配置します。
    /// Copies the originals of files whose content could not be extracted into the output, preserving
    /// the original directory structure. They go into a dedicated subfolder so that files with the
    /// same name do not collide and do not mix with the generated Markdown.
    /// </summary>
    /// <param name="skippedFiles">コピー対象のファイルパス / The paths of the files to copy.</param>
    private void CopySkippedFiles(IReadOnlyList<string> skippedFiles)
    {
        if (skippedFiles.Count == 0) return;

        Console.WriteLine("📎 Copying unsupported files...");

        string destinationRoot = Path.Combine(_settings.OutputPath, AnalyzerSettings.SkippedFilesDirectoryName);
        int copiedCount = 0;

        foreach (var sourcePath in skippedFiles)
        {
            try
            {
                string relativePath = Path.GetRelativePath(_settings.ProjectPath, sourcePath);
                string destinationPath = Path.Combine(destinationRoot, relativePath);

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, true);
                copiedCount++;
            }
            catch (Exception ex)
            {
                // 1件のコピー失敗で分析全体を止めないよう、警告だけ出して続行する
                // Warn and continue, so that a single failed copy does not abort the whole analysis.
                Console.WriteLine($"   [Warning] Could not copy '{Path.GetFileName(sourcePath)}': {ex.Message}");
            }
        }

        Console.WriteLine($"   -> Success: Copied {copiedCount} file(s) into '{AnalyzerSettings.SkippedFilesDirectoryName}/'\n");
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
    
    /// <summary>
    /// リソースを解放します。一時ディレクトリが存在する場合は削除します。
    /// Releases resources. Deletes the temporary directory if it exists.
    /// </summary>
    public void Dispose()
    {
        // 一時フォルダが設定されていて、存在する場合のみ削除を実行
        // Execute deletion only if a temporary folder is set and exists.
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

    /// <summary>
    /// 指定されたディレクトリ内のすべてのファイルの読み取り専用属性を削除します。
    /// Removes read-only attributes from all files in the specified directory.
    /// </summary>
    /// <param name="directory">処理対象のディレクトリ / The directory to process.</param>
    private void RemoveReadOnlyAttributes(DirectoryInfo directory)
    {
        foreach (var file in directory.GetFiles("*", SearchOption.AllDirectories))
        {
            file.Attributes &= ~FileAttributes.ReadOnly;
        }
    }
}