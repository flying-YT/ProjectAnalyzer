namespace ProjectAnalyzer.Core;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// プロジェクト分析のための設定を保持するクラスです。
/// A class that holds settings for project analysis.
/// </summary>
public class AnalyzerSettings
{
    public string ProjectPath { get; }
    public string OutputPath { get; }
    public ISet<string> IgnoreList { get; }
    
    /// <summary>
    /// ファイルに出力するかどうかを示すフラグ。
    /// A flag indicating whether to output to a file.
    /// </summary>
    public bool OutputToFile { get; }

    /// <summary>
    /// Markdown出力時にプログラムコードを示す「```」を省略するかどうかを示すフラグ。
    /// A flag indicating whether to omit "```" indicating program code when outputting Markdown.
    /// </summary>
    public bool OmitCodeBlockTicks { get; }

    public AnalyzerSettings(string projectPath, string outputPath, ISet<string> ignoreList, bool outputToFile = true, bool omitCodeBlockTicks = false)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        OutputPath = string.IsNullOrWhiteSpace(outputPath) ? string.Empty : Path.GetFullPath(outputPath);
        IgnoreList = ignoreList;
        OutputToFile = outputToFile;
        OmitCodeBlockTicks = omitCodeBlockTicks;
    }
}