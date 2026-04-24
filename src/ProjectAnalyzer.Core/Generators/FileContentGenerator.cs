using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ExcelDataReader;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml.Presentation;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;
using Tesseract;

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
        // Register encoding provider to make ExcelDataReader work in .NET Core and later.
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
            // Ignore files that were skipped (e.g., images, read errors).
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

    /// <summary>
    /// 指定されたパス以下のすべてのファイルを取得します（除外リストを考慮）。
    /// Gets all files under the specified path (considering the ignore list).
    /// </summary>
    /// <param name="path">検索を開始するディレクトリパス / The directory path to start searching.</param>
    /// <returns>ファイルパスのリスト / A list of file paths.</returns>
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
            // Special handling for Excel files.
            if (extension == ".xlsx" || extension == ".xls" || extension == ".xlsm")
            {
                content = ReadExcelFile(filePath);
                if (extension == ".xlsx" || extension == ".xlsm")
                {
                    // ★ メソッド名を変更し、画像OCRも実行させる
                    string shapesText = ExtractExcelShapesAndImagesText(filePath);
                    if (!string.IsNullOrWhiteSpace(shapesText))
                    {
                        content += "\n### [Shapes, TextBoxes & Images]\n" + shapesText;
                    }
                }
            }
            // Wordファイル(.docx)の場合の特別処理
            // Special handling for Word files (.docx).
            else if (extension == ".docx")
            {
                content = ReadWordFile(filePath);
                if (string.IsNullOrEmpty(content)) return string.Empty;
            }
            // PowerPointファイル(.pptx)の場合の特別処理 (追加)
            // Special handling for PowerPoint files (.pptx).
            else if (extension == ".pptx")
            {
                content = ReadPowerPointFile(filePath);
                if (string.IsNullOrEmpty(content)) return string.Empty;
            }
            else
            {
                // 通常のテキストファイルとして読み込み
                // Read as a normal text file.
                content = File.ReadAllText(filePath);
                language = LanguageMapper.GetLanguage(extension);
            }

            // HTMLタグを無害化するオプションが有効な場合
            // "if (a < b)" 等を除外するため、"<" の直後にアルファベットか "/" が続くパターンのみ置換する
            if (_settings.SanitizeHtmlTags)
            {
                // [] ではなく全角の ＜ ＞ に置換してMarkdownのパース誤動作を防ぐ
                content = Regex.Replace(content, @"<(/?[a-zA-Z][^<>]*)>", "＜$1＞");
            }

            // インデントを削除するオプションが有効な場合
            // Markdownで4スペースがコードブロックとして解釈されるのを防ぐ
            if (_settings.RemoveIndent)
            {
                // 複数行モード(?m)で行頭の空白文字(スペース、タブ)を削除
                content = Regex.Replace(content, @"(?m)^[ \t]+", "");
            }

            // NotebookLM対策：ツールが出力する details/summary タグも置換対象にする
            string detailsOpen = _settings.SanitizeHtmlTags ? "＜details＞" : "<details>";
            string detailsClose = _settings.SanitizeHtmlTags ? "＜/details＞" : "</details>";
            string summaryText = _settings.SanitizeHtmlTags ? "＜summary＞View content＜/summary＞" : "<summary>View content</summary>";

            sb.AppendLine($"**File Content:**");
            sb.AppendLine(detailsOpen);
            sb.AppendLine(summaryText);
            sb.AppendLine();
            sb.AppendLine(content);
            sb.AppendLine(detailsClose);
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
    /// Reads an Excel file and returns it as Markdown formatted text.
    /// </summary>
   private string ReadExcelFile(string filePath)
    {
        var sb = new StringBuilder();
        
        using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read))
        {
            // ExcelReaderFactory を使用してストリームから読み込む
            // Read from stream using ExcelReaderFactory.
            using (var reader = ExcelReaderFactory.CreateReader(stream))
            {
                // DataSet に変換することで、シートごとに DataTable として扱える
                // Convert to DataSet to handle each sheet as a DataTable.
                var result = reader.AsDataSet();

                foreach (DataTable table in result.Tables)
                {
                    // シート名を見出しにする
                    // Use sheet name as a heading.
                    sb.AppendLine($"### {table.TableName}");

                    foreach (DataRow row in table.Rows)
                    {
                        var rowValues = new List<string>();
                        foreach (var item in row.ItemArray)
                        {
                            // nullやDBNullを空文字に変換
                            // Convert null or DBNull to empty string.
                            string cellValue = item?.ToString() ?? "";
                            
                            // セル内に改行やタブが含まれているとMarkdownのレイアウトが崩れる可能性があるため、空白に置換
                            // Replace newlines and tabs with spaces to prevent Markdown layout issues.
                            cellValue = cellValue.Replace("\n", " ").Replace("\r", "").Replace("\t", " ");
                            rowValues.Add(cellValue);
                        }
                        
                        // 完全に空の行はスキップ（Excelでは未使用のセルも読み込まれることがあるため）
                        // Skip completely empty rows (as Excel may read unused cells).
                        if (rowValues.All(string.IsNullOrWhiteSpace)) continue;

                        // AIが文脈を解釈しやすいように、カンマ区切り（CSV風）で結合
                        // Join with commas (CSV style) to make context easier for AI to interpret.
                        sb.AppendLine(string.Join(", ", rowValues));
                    }
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

/// <summary>
    /// Excelファイル(.xlsx, .xlsm)から図形やテキストボックスの文字、および埋め込み画像のOCRテキストを抽出します。(デバッグ出力版)
    /// </summary>
    private string ExtractExcelShapesAndImagesText(string filePath)
    {
        var sb = new StringBuilder();
        try
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(filePath, false))
            {
                if (doc.WorkbookPart?.WorksheetParts == null) return string.Empty;
                
                int imageCount = 1;

                foreach (var sheetPart in doc.WorkbookPart.WorksheetParts)
                {
                    if (sheetPart.DrawingsPart != null)
                    {
                        // 1. 図形やテキストボックス内の文字データを抽出
                        foreach (var text in sheetPart.DrawingsPart.WorksheetDrawing.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
                        {
                            if (!string.IsNullOrWhiteSpace(text.Text)) sb.AppendLine(text.Text);
                        }

                        // 2. 埋め込み画像の存在チェックとOCR
                        if (sheetPart.DrawingsPart.ImageParts != null && sheetPart.DrawingsPart.ImageParts.Any())
                        {
                            if (!_settings.EnableOcr)
                            {
                                sb.AppendLine($"\n--- ⚠️ 画像が見つかりましたが、OCRが無効(--enable-ocrなし)のためスキップしました ---");
                                continue;
                            }

                            foreach (var imagePart in sheetPart.DrawingsPart.ImageParts)
                            {
                                try
                                {
                                    using (var stream = imagePart.GetStream())
                                    using (var ms = new MemoryStream())
                                    {
                                        stream.CopyTo(ms);
                                        byte[] imageBytes = ms.ToArray();
                                        
                                        // 拡張子がないとOCRエンジン(Leptonica)が画像フォーマットを誤認するため明示的に付与
                                        string tempFilePath = Path.GetTempFileName();
                                        string ext = imagePart.ContentType.Contains("jpeg") ? ".jpg" : ".png";
                                        string newTempFilePath = tempFilePath + ext;
                                        
                                        // .tmpファイルをリネームしてから書き込む
                                        File.Move(tempFilePath, newTempFilePath);
                                        File.WriteAllBytes(newTempFilePath, imageBytes);
                                        
                                        // OCR実行
                                        string ocrText = ReadImageTextWithOcr(newTempFilePath);
                                        
                                        // デバッグのため、エラーでも空でも強制的に出力する
                                        sb.AppendLine($"\n--- Embedded Image {imageCount} (ContentType: {imagePart.ContentType}) ---");
                                        sb.AppendLine(string.IsNullOrWhiteSpace(ocrText) ? "[No Text Found]" : ocrText);
                                        
                                        imageCount++;
                                        
                                        if (File.Exists(newTempFilePath)) File.Delete(newTempFilePath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    sb.AppendLine($"\n--- Embedded Image {imageCount} Error ---");
                                    sb.AppendLine(ex.Message);
                                    imageCount++;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"\n[Excel Extract Error: {ex.Message}]");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Tesseractを使用して画像からテキストを抽出します。
    /// </summary>
    private string ReadImageTextWithOcr(string filePath)
    {
        try
        {
            // まずは Tesseract.NET (Windows環境など) での実行を試す
            string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            
            if (Directory.Exists(tessDataPath))
            {
                using (var engine = new TesseractEngine(tessDataPath, "jpn+eng", EngineMode.Default))
                using (var img = Pix.LoadFromFile(filePath))
                using (var page = engine.Process(img))
                {
                    return page.GetText().Trim();
                }
            }
            return ReadImageTextWithCommandLine(filePath);
        }
        catch
        {
            // ネイティブDLLの読み込みエラー等(Linux環境)が発生した場合はOSのコマンドにフォールバックする
            return ReadImageTextWithCommandLine(filePath);
        }
    }

    /// <summary>
    /// OSにインストールされている Tesseract コマンドを直接呼び出してOCRを実行します。(Linux向けフォールバック)
    /// </summary>
    private string ReadImageTextWithCommandLine(string filePath)
    {
        try
        {
            // tesseractコマンドの出力先(拡張子.txtが自動で付くため拡張子なしのパスを指定)
            string tempOutputFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            
            // コマンド引数: [入力画像パス] [出力テキストパス] -l jpn+eng
            var processInfo = new System.Diagnostics.ProcessStartInfo("tesseract", $"\"{filePath}\" \"{tempOutputFile}\" -l jpn+eng")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            process?.WaitForExit();

            string resultFilePath = tempOutputFile + ".txt";
            
            if (File.Exists(resultFilePath))
            {
                string text = File.ReadAllText(resultFilePath);
                File.Delete(resultFilePath); // 読み終わった一時ファイルを削除
                return text.Trim();
            }
            
            string error = process?.StandardError.ReadToEnd() ?? "Unknown error";
            return $"[OCR Command Error: {error}]";
        }
        catch (Exception ex)
        {
            return $"[OCR Fallback Error: {ex.Message}]";
        }
    }

    /// <summary>
    /// Wordファイル(.docx)を読み込み、プレーンテキストとして返します。
    /// Reads a Word file (.docx) and returns it as plain text.
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
                    // Sequentially extract paragraphs (Paragraph) in the document.
                    foreach (var para in body.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
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

    /// <summary>
    /// PowerPointファイル(.pptx)を読み込み、スライドごとのテキストを抽出して返します。
    /// Reads a PowerPoint file (.pptx) and returns extracted text per slide.
    /// </summary>
    private string ReadPowerPointFile(string filePath)
    {
        var sb = new StringBuilder();
        try
        {
            using (PresentationDocument presentationDoc = PresentationDocument.Open(filePath, false))
            {
                var presentationPart = presentationDoc.PresentationPart;
                if (presentationPart != null && presentationPart.Presentation != null)
                {
                    var slideIdList = presentationPart.Presentation.SlideIdList;
                    if (slideIdList != null)
                    {
                        int slideIndex = 1;
                        // スライドを順番に処理
                        foreach (SlideId slideId in slideIdList.Elements<SlideId>())
                        {
                            if (slideId.RelationshipId != null)
                            {
                                SlidePart slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId.Value!);
                                if (slidePart != null && slidePart.Slide != null)
                                {
                                    sb.AppendLine($"### Slide {slideIndex}");
                                    
                                    // スライド内のテキスト要素(Drawing.Text)をすべて抽出
                                    foreach (var text in slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
                                    {
                                        if (!string.IsNullOrWhiteSpace(text.Text))
                                        {
                                            sb.AppendLine(text.Text);
                                        }
                                    }
                                    sb.AppendLine();
                                }
                            }
                            slideIndex++;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [Warning] Could not read PowerPoint file '{Path.GetFileName(filePath)}': {ex.Message}");
            return string.Empty;
        }

        return sb.ToString();
    }

    /// <summary>
    /// プロジェクト内の各ファイルに対して、個別のMarkdownコンテンツを生成します。
    /// Generates individual Markdown content for each file in the project.
    /// </summary>
    /// <returns>相対ファイルパス（拡張子.md付き）とMarkdownコンテンツのペアのリスト。 / A list of pairs of relative file paths (with .md extension) and Markdown content.</returns>
    public List<(string RelativePath, string Content)> GeneratePerFile()
    {
        var fileContents = new List<(string, string)>();
        var allFiles = GetAllFiles(_settings.ProjectPath);

        foreach (var file in allFiles)
        {
            string fileMarkdown = GenerateMarkdownForFile(file);
            if (string.IsNullOrEmpty(fileMarkdown)) continue;

            // 元の相対パスを取得し、末尾に .md を追加する（例: "src/Utils.cs" -> "src/Utils.cs.md"）
            // Get the original relative path and append .md to the end (e.g., "src/Utils.cs" -> "src/Utils.cs.md").
            string relativePath = Path.GetRelativePath(_settings.ProjectPath, file);
            string markdownRelativePath = relativePath + ".md";

            fileContents.Add((markdownRelativePath, fileMarkdown));
        }

        return fileContents;
    }
}