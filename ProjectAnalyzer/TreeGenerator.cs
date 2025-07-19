using System.Text;

/// <summary>
/// プロジェクトのフォルダ構造をツリー形式で表現する文字列を生成するクラスです。
/// A class that generates a string representing the project's folder structure in a tree format.
/// </summary>
public class TreeGenerator
{
    private readonly AnalyzerSettings _settings;

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
        GenerateRecursive(rootDirInfo, "", sb, true);
        return sb.ToString();
    }

    /// <summary>
    /// ディレクトリ構造を再帰的に探索し、ツリー形式の文字列を構築します。
    /// Recursively traverses the directory structure to build the tree-formatted string.
    /// </summary>
    /// <param name="directory">処理対象のディレクトリ。/ The directory to process.</param>
    /// <param name="indent">現在のインデント文字列。/ The current indentation string.</param>
    /// <param name="sb">ツリー文字列を構築するための StringBuilder。/ The StringBuilder to build the tree string.</param>
    /// <param name="isParentLast">親ディレクトリが、その兄弟の中で最後の要素であったかどうかを示すフラグ。/ A flag indicating whether the parent directory was the last element among its siblings.</param>
    private void GenerateRecursive(DirectoryInfo directory, string indent, StringBuilder sb, bool isLast)
    {
        var subDirectories = directory.GetDirectories()
                                      .Where(d => !_settings.IgnoreList.Contains(d.Name))
                                      .OrderBy(d => d.Name)
                                      .ToList();
        var files = directory.GetFiles()
                             .Where(f => !_settings.IgnoreList.Contains(f.Name))
                             .OrderBy(f => f.Name)
                             .ToList();

        string newIndent = indent + (isLast ? "    " : "│   ");

        for (int i = 0; i < subDirectories.Count; i++)
        {
            var subDir = subDirectories[i];
            bool isLastEntry = (i == subDirectories.Count - 1) && (files.Count == 0);
            sb.AppendLine($"{indent}└── {(isLastEntry ? "└── " : "├── ")}{subDir.Name}");
            GenerateRecursive(subDir, newIndent, sb, isLastEntry);
        }

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            bool isLastEntry = (i == files.Count - 1);
            sb.AppendLine($"{indent}└── {(isLastEntry ? "└── " : "├── ")}{file.Name}");
        }
    }
}