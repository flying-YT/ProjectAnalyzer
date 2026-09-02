using System.Text;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;

namespace ProjectAnalyzer.Core.Generators;

/// <summary>
/// プロジェクトのフォルダ構造をツリー形式で表現する文字列を生成するクラスです。
/// A class that generates a string representing the project's folder structure in a tree format.
/// </summary>
public class TreeGenerator
{
    private readonly AnalyzerSettings _settings;

    /// <summary>
    /// シンボリックリンクであることをツリー上に示す注記です。
    /// The note shown in the tree to mark an entry as a symbolic link.
    /// </summary>
    private const string SymbolicLinkNote = " [symbolic link, not followed]";

    /// <summary>
    /// TreeGenerator クラスの新しいインスタンスを初期化します。
    /// Initializes a new instance of the TreeGenerator class.
    /// </summary>
    /// <param name="settings">分析に使用する設定。/ The settings to use for the analysis.</param>
    public TreeGenerator(AnalyzerSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// プロジェクトのフォルダ構造を表すツリー文字列を生成します。
    /// Generates the tree string representing the project's folder structure.
    /// </summary>
    /// <returns>生成されたツリー構造の文字列。/ The generated tree structure as a string.</returns>
    public string Generate()
    {
        var sb = new StringBuilder();
        var rootDirInfo = new DirectoryInfo(_settings.ProjectPath);
        sb.AppendLine(rootDirInfo.Name);
        
        // 修正: 第4引数(isLast)は不要になったため削除
        // Fix: Removed the 4th argument (isLast) as it is no longer needed.
        GenerateRecursive(rootDirInfo, "", sb);
        
        return sb.ToString();
    }

    /// <summary>
    /// ディレクトリ構造を再帰的に探索し、ツリー形式の文字列を構築します。
    /// Recursively traverses the directory structure to build the tree-formatted string.
    /// </summary>
    /// <param name="directory">処理対象のディレクトリ。/ The directory to process.</param>
    /// <param name="indent">現在のインデント文字列。/ The current indentation string.</param>
    /// <param name="sb">ツリー文字列を構築するための StringBuilder。/ The StringBuilder to build the tree string.</param>
    private void GenerateRecursive(DirectoryInfo directory, string indent, StringBuilder sb)
    {
        var subDirectories = directory.GetDirectories()
                                      .Where(d => !_settings.IgnoreList.Contains(d.Name))
                                      .OrderBy(d => d.Name)
                                      .ToList();
        var files = directory.GetFiles()
                             .Where(f => !_settings.IgnoreList.Contains(f.Name))
                             .OrderBy(f => f.Name)
                             .ToList();

        for (int i = 0; i < subDirectories.Count; i++)
        {
            var subDir = subDirectories[i];
            bool isLastEntry = (i == subDirectories.Count - 1) && (files.Count == 0);

            // シンボリックリンクは辿らない。リンク先が対象フォルダの外にあると解析対象外のファイルまで
            // 読み取ってしまい、ループするリンクでは走査が終わらなくなるため。
            // 存在自体は隠さず、内容を辿っていないことが分かるよう注記を添える。
            // Symbolic links are not followed. A link pointing outside the analyzed folder would pull in
            // files that are not part of it, and a link that loops would make the traversal never end.
            // The entry is still listed, with a note showing that its content was not walked.
            if (SymbolicLinkDetector.IsSymbolicLink(subDir))
            {
                sb.AppendLine($"{indent}{(isLastEntry ? "└── " : "├── ")}{subDir.Name}{SymbolicLinkNote}");
                continue;
            }

            // 1. 現在のディレクトリの枝を描画
            // 1. Draw the branch for the current directory.
            sb.AppendLine($"{indent}{(isLastEntry ? "└── " : "├── ")}{subDir.Name}");

            // 2. 次の階層へ渡すインデントを作成（自分が最後なら空白、続くなら縦線）
            // 2. Create indentation to pass to the next level (space if last, vertical line otherwise).
            string nextIndent = indent + (isLastEntry ? "    " : "│   ");

            // 3. 子要素の再帰処理
            // 3. Recursive processing of child elements.
            GenerateRecursive(subDir, nextIndent, sb);
        }

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            bool isLastEntry = (i == files.Count - 1);

            // ファイルのリンクも同様に、実体を読まずに存在だけを示す
            // A file link is listed the same way: its presence is shown, its target is not read.
            string note = SymbolicLinkDetector.IsSymbolicLink(file) ? SymbolicLinkNote : string.Empty;

            // 1. 現在のファイルの枝を描画
            // 1. Draw the branch for the current file.
            sb.AppendLine($"{indent}{(isLastEntry ? "└── " : "├── ")}{file.Name}{note}");
        }
    }
}