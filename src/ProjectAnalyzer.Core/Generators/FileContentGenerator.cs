using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using ExcelDataReader;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;

namespace ProjectAnalyzer.Core.Generators;

/// <summary>
/// プロジェクト内の各ファイルの内容をMarkdown形式で生成するクラスです。
/// A class that generates the content of each file in the project in Markdown format.
/// </summary>
public class FileContentGenerator
{
    private readonly AnalyzerSettings _settings;
    private const long MaxFileSize = 4 * 1024 * 1024; // 4MB

    /// <summary>
    /// FileContentGenerator クラスの新しいインスタンスを初期化します。
    /// Initializes a new instance of the FileContentGenerator class.
    /// </summary>
    /// <param name="settings">分析に使用する設定。/ The settings to use for the analysis.</param>
    public FileContentGenerator(AnalyzerSettings settings)
    {
        _settings = settings;
        
        // .NET Core 以降で ExcelDataReader を動作させるためのエンコーディングプロバイダ登録
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// プロジェクト内のすべてのファイルに対するMarkdownコンテンツのリストを生成します。
    /// Generates a list of Markdown content for all files in the project.
    /// </summary>
    /// <returns>生成されたMarkdownコンテンツ文字列のリスト。/ The list of generated Markdown content strings.</returns>
    public List<string> Generate()
    {
        var fileContents = new List<string>();
        var sb = new StringBuilder();
        sb.AppendLine("# \U0001f4c4 Project Context");
        sb.AppendLine();

        long currentSize = 0;
        var allFiles = GetAllFiles(_settings.ProjectPath);

        foreach (var file in allFiles)
        {
            string fileMarkdown = GenerateMarkdownForFile(file);
            
            // 処理をスキップしたファイル（画像や読み込みエラー等）は無視する
            if (string.IsNullOrEmpty(fileMarkdown)) continue;

            long fileSize = Encoding.UTF8.GetByteCount(fileMarkdown);

            if (currentSize + fileSize > MaxFileSize && sb.Length > 0)
            {
                fileContents.Add(sb.ToString());
                sb.Clear();
                sb.AppendLine("# \U0001f4c4 Project Context (続き)");
                sb.AppendLine();
                currentSize = 0;
            }

            sb.Append(fileMarkdown);
            currentSize += fileSize;
        }

        if (sb.Length > 0)
        {
            fileContents.Add(sb.ToString());
        }

        return fileContents;
    }

    private List<string> GetAllFiles(string path)
    {
        var files = new List<string>();

        foreach (var file in Directory.GetFiles(path).OrderBy(f => f))
        {
            if (!_settings.IgnoreList.Contains(Path.GetFileName(file)))
            {
                files.Add(file);
            }
        }

        foreach (var dir in Directory.GetDirectories(path).OrderBy(d => d))
        {
            if (!_settings.IgnoreList.Contains(Path.GetFileName(dir)))
            {
                files.AddRange(GetAllFiles(dir));
            }
        }

        return files;
    }

    /// <summary>
    /// 単一のソースファイルからMarkdownコンテンツを生成します。
    /// Generates Markdown content from a single source file.
    /// </summary>
    /// <param name="filePath">処理対象のソースファイルのパス。/ The path of the source file to process.</param>
    /// <returns>生成されたMarkdownコンテンツ。/ The generated Markdown content.</returns>
    private string GenerateMarkdownForFile(string filePath)
    {
        try
        {
            var sb = new StringBuilder();
            string relativePath = Path.GetRelativePath(_settings.ProjectPath, filePath);
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"## {Path.GetFileName(filePath)}");
            sb.AppendLine();
            sb.AppendLine($"**Relative Path:** `{relativePath}`");
            sb.AppendLine();

            string content;
            string language = "";

            // Excelファイルの場合の特別処理
            if (extension == ".xlsx" || extension == ".xls" || extension == ".xlsm")
            {
                content = ReadExcelFile(filePath);
                if (string.IsNullOrEmpty(content)) return string.Empty; // 読み込めなかった場合は空文字
            }
            // Wordファイル(.docx)の場合の特別処理
            else if (extension == ".docx")
            {
                content = ReadWordFile(filePath);
                if (string.IsNullOrEmpty(content)) return string.Empty;
            }
            else
            {
                // 通常のテキストファイルとして読み込み
                content = File.ReadAllText(filePath);
                language = LanguageMapper.GetLanguage(extension);
            }

            sb.AppendLine($"**File Content:**");
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>View content</summary>");
            sb.AppendLine();
            sb.AppendLine(content);
            sb.AppendLine("</details>");
            sb.AppendLine();

            if (!_settings.OmitCodeBlockTicks)
            {
                sb.AppendLine(string.IsNullOrEmpty(language) ? "```" : $"```{language}");
                sb.AppendLine(content);
                sb.AppendLine("```");
            }
            sb.AppendLine();

            return sb.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [Warning] Could not process file '{Path.GetFileName(filePath)}': {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Excelファイルを読み込み、マークダウン形式のテキストとして返します。
    /// </summary>
    private string ReadExcelFile(string filePath)
    {
        var sb = new StringBuilder();
        
        using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read))
        {
            // ExcelReaderFactory を使用してストリームから読み込む
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                // DataSet に変換することで、シートごとに DataTable として扱える
                var result = reader.AsDataSet();

                foreach (DataTable table in result.Tables)
                {
                    // シート名を見出しにする
                    sb.AppendLine($"### {table.TableName}");

                    foreach (DataRow row in table.Rows)
                    {
                        var rowValues = new List<string>();
                        foreach (var item in row.ItemArray)
                        {
                            // nullやDBNullを空文字に変換
                            string cellValue = item?.ToString() ?? "";
                            
                            // セル内に改行やタブが含まれているとMarkdownのレイアウトが崩れる可能性があるため、空白に置換
                            cellValue = cellValue.Replace("\n", " ").Replace("\r", "").Replace("\t", " ");
                            rowValues.Add(cellValue);
                        }
                        
                        // 完全に空の行はスキップ（Excelでは未使用のセルも読み込まれることがあるため）
                        if (rowValues.All(string.IsNullOrWhiteSpace)) continue;

                        // AIが文脈を解釈しやすいように、カンマ区切り（CSV風）で結合
                        sb.AppendLine(string.Join(", ", rowValues));
                    }
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Wordファイル(.docx)を読み込み、プレーンテキストとして返します。
    /// </summary>
    private string ReadWordFile(string filePath)
    {
        var sb = new StringBuilder();
        try
        {
            using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
            {
                var body = wordDoc.MainDocumentPart?.Document?.Body;
                if (body != null)
                {
                    // ドキュメント内の段落(Paragraph)を順番に抽出
                    foreach (var para in body.Descendants<Paragraph>())
                    {
                        sb.AppendLine(para.InnerText);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [Warning] Could not read Word file '{Path.GetFileName(filePath)}': {ex.Message}");
            return string.Empty;
        }

        return sb.ToString();
    }
}
