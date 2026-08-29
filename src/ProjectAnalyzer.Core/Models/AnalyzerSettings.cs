using System.Collections.Generic;
using System.IO;

namespace ProjectAnalyzer.Core.Models;
/// <summary>
/// プロジェクト分析のための設定を保持するクラスです。
/// A class that holds settings for project analysis.
/// </summary>
public class AnalyzerSettings
{
    /// <summary>
    /// 分析対象のプロジェクトパス / The project path to analyze.
    /// </summary>
    public string ProjectPath { get; }
    
    /// <summary>
    /// 出力先のパス / The output path.
    /// </summary>
    public string OutputPath { get; }
    
    /// <summary>
    /// 無視するファイルやディレクトリのリスト / The list of files and directories to ignore.
    /// </summary>
    public ISet<string> IgnoreList { get; }
    
    /// <summary>
    /// ファイルに出力するかどうか / Whether to output to a file.
    /// </summary>
    public bool OutputToFile { get; }
    
    /// <summary>
    /// コードブロックのバッククォートを省略するかどうか / Whether to omit code block backticks.
    /// </summary>
    public bool OmitCodeBlockTicks { get; }
    
    /// <summary>
    /// ファイルごとに個別に出力するかどうか / Whether to output per file individually.
    /// </summary>
    public bool OutputPerFile { get; }
    
    /// <summary>
    /// HTMLタグを無害な形式（[tag]）に置換するかどうか / Whether to sanitize HTML tags.
    /// </summary>
    public bool SanitizeHtmlTags { get; }

    /// <summary>
    /// 行頭のインデントをすべて削除するかどうか / Whether to remove all line indents.
    /// </summary>
    public bool RemoveIndent { get; }

    /// <summary>
    /// 画像ファイルに対するOCRを有効にするかどうか / Whether to enable OCR for image files.
    /// </summary>
    public bool EnableOcr { get; }

    /// <summary>
    /// 出力Markdown 1ファイルあたりのサイズのしきい値（バイト） / The size threshold per output Markdown file, in bytes.
    /// </summary>
    public long MaxOutputSize { get; }

    /// <summary>
    /// 出力Markdown 1ファイルあたりのサイズの既定のしきい値（4MB） / The default size threshold per output Markdown file (4MB).
    /// </summary>
    public const long DefaultMaxOutputSize = 4 * 1024 * 1024;

    /// <summary>
    /// 内容を抽出できないファイルの原本をコピーする出力先フォルダ名
    /// The name of the output folder that receives copies of files whose content cannot be extracted.
    /// </summary>
    public const string SkippedFilesDirectoryName = "02_SkippedFiles";

    /// <summary>
    /// 一時的なクローンパス（Gitリポジトリの場合） / The temporary clone path (for Git repositories).
    /// </summary>
    public string? TempClonePath { get; }

    /// <summary>
    /// AnalyzerSettings クラスの新しいインスタンスを初期化します。
    /// Initializes a new instance of the AnalyzerSettings class.
    /// </summary>
    /// <param name="projectPath">プロジェクトパス / Project path</param>
    /// <param name="outputPath">出力パス / Output path</param>
    /// <param name="ignoreList">除外リスト / Ignore list</param>
    /// <param name="outputToFile">ファイル出力フラグ / File output flag</param>
    /// <param name="omitCodeBlockTicks">コードブロック省略フラグ / Omit code block ticks flag</param>
    /// <param name="outputPerFile">個別出力フラグ / Output per file flag</param>
    /// <param name="tempClonePath">一時クローンパス / Temporary clone path</param>
    /// <param name="sanitizeHtmlTags">HTMLタグ無害化フラグ / Sanitize HTML tags flag</param>
    /// <param name="removeIndent">インデント削除フラグ / Remove indent flag</param>
    /// <param name="enableOcr">OCR有効化フラグ / Enable OCR flag</param>
    /// <param name="maxOutputSize">出力Markdown 1ファイルあたりのサイズのしきい値（バイト）。0以下を指定した場合は既定値を使用します。/ The size threshold per output Markdown file in bytes. Values of zero or less fall back to the default.</param>
    public AnalyzerSettings(string projectPath, string outputPath, ISet<string> ignoreList, bool outputToFile = true, bool omitCodeBlockTicks = false, bool outputPerFile = false, string? tempClonePath = null, bool sanitizeHtmlTags = false, bool removeIndent = false, bool enableOcr = false, long maxOutputSize = DefaultMaxOutputSize)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        OutputPath = string.IsNullOrWhiteSpace(outputPath) ? string.Empty : Path.GetFullPath(outputPath);
        IgnoreList = ignoreList;
        OutputToFile = outputToFile;
        OmitCodeBlockTicks = omitCodeBlockTicks;
        OutputPerFile = outputPerFile;
        TempClonePath = tempClonePath;
        SanitizeHtmlTags = sanitizeHtmlTags;
        RemoveIndent = removeIndent;
        EnableOcr = enableOcr;

        // 0以下だと分割が無限に発生するため、不正な値は既定値へ丸める
        // Values of zero or less would cause endless splitting, so invalid values fall back to the default.
        MaxOutputSize = maxOutputSize > 0 ? maxOutputSize : DefaultMaxOutputSize;
    }
}