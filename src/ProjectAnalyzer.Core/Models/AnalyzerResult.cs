using System.Collections.Generic;

namespace ProjectAnalyzer.Core.Models;
/// <summary>
/// プロジェクトの分析結果を保持するクラスです。
/// A class that holds the analysis results of the project.
/// </summary>
public class AnalyzerResult
{
    /// <summary>
    /// プロジェクトのフォルダ構造ツリーの文字列
    /// The string representing the project's folder structure tree.
    /// </summary>
    public string ProjectTree { get; set; } = string.Empty;

    /// <summary>
    /// 各ファイルの内容をまとめたコンテンツのリスト
    /// A list of contents summarizing the contents of each file.
    /// </summary>
    public List<string> ProjectContexts { get; set; } = new();

    /// <summary>
    /// 個別のファイルごとのコンテキスト（相対パスと内容）のリスト
    /// A list of contexts (relative path and content) for each individual file.
    /// </summary>
    public List<(string RelativePath, string Content)> IndividualFileContexts { get; set; } = new();
}