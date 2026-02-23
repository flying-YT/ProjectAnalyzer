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
    public bool OutputToFile { get; }
    public bool OmitCodeBlockTicks { get; }
    
    public string? TempClonePath { get; }

    public AnalyzerSettings(string projectPath, string outputPath, ISet<string> ignoreList, bool outputToFile = true, bool omitCodeBlockTicks = false, string? tempClonePath = null)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        OutputPath = string.IsNullOrWhiteSpace(outputPath) ? string.Empty : Path.GetFullPath(outputPath);
        IgnoreList = ignoreList;
        OutputToFile = outputToFile;
        OmitCodeBlockTicks = omitCodeBlockTicks;
        TempClonePath = tempClonePath;
    }
}