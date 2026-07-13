using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    /// 静的コンストラクタ。OCR(Tesseract)の内部OpenMP並列を1スレッドに制限します。
    /// Tesseractは1画像あたり全コアを使って並列処理するため、本ツールのファイル単位並列と
    /// 二重になるとCPUのオーバーサブスクリプション（スレッド過多）が発生し、かえって低速化します。
    /// 並列化をファイル単位に一本化するため、ユーザーが明示指定していない場合のみ1スレッドに固定します。
    /// この設定はプロセス内のlibtesseractと、フォールバックで起動するtesseract子プロセスの双方に効きます。
    /// Static constructor. Caps Tesseract's internal OpenMP parallelism to a single thread.
    /// Tesseract parallelizes each image across all cores; combined with this tool's per-file
    /// parallelism it causes CPU oversubscription and slowdown. To keep parallelism at the file
    /// level only, we pin the thread count to 1 unless the user has set it explicitly. This applies
    /// to both the in-process libtesseract and the fallback tesseract child processes.
    /// </summary>
    static FileContentGenerator()
    {
        bool userConfigured =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OMP_THREAD_LIMIT"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OMP_NUM_THREADS"));

        if (!userConfigured)
        {
            Environment.SetEnvironmentVariable("OMP_THREAD_LIMIT", "1");
        }
    }

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
        var allFiles = GetAllFiles(_settings.ProjectPath);

        // 【map】各ファイルのMarkdown生成をファイル単位で並列実行する（OCR等の重い処理を高速化）。
        // 生成結果はインデックス順に格納するため、出力順序は逐次実行時と完全に一致する。
        // [map] Generate the Markdown for each file in parallel (speeds up heavy work such as OCR).
        // Results are stored by index, so the output order stays identical to sequential execution.
        var markdownByIndex = GenerateMarkdownInParallel(allFiles);

        // 【reduce】生成済みの結果を元の順序どおりに連結・サイズ分割する（逐次処理・従来と同一ロジック）。
        // [reduce] Concatenate and split the pre-generated results in original order (sequential, same logic as before).
        var fileContents = new List<string>();
        var sb = new StringBuilder();
        sb.AppendLine("# \U0001f4c4 Project Context");
        sb.AppendLine();

        long currentSize = 0;

        foreach (var fileMarkdown in markdownByIndex)
        {
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
                                // 共通のOCR処理メソッドを呼び出し
                                sb.Append(ProcessImagePartOcr(imagePart, ref imageCount));
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
            // 実行されているアプリケーションのベースディレクトリ（dllやexeが展開されている場所）を取得
            string baseDir = AppContext.BaseDirectory;
            string tessDataPath = Path.Combine(baseDir, "tessdata");     

            if (Directory.Exists(tessDataPath))
            {
                using (var engine = new TesseractEngine(tessDataPath, "jpn+eng", EngineMode.Default))
                using (var img = Pix.LoadFromFile(filePath))
                using (var page = engine.Process(img))
                {
                    string text = page.GetText().Trim();
                    
                    // ★追加: 日本語（非ASCII文字）に隣接する空白（スペース・タブ）を除去する（改行は維持）
                    text = Regex.Replace(text, @"(?<=[^\x00-\x7F])[ \t　]+|[ \t　]+(?=[^\x00-\x7F])", "");
                    
                    return text;
                }
            }
            return ReadImageTextWithCommandLine(filePath);
        }
        catch
        {
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

            // 静的コンストラクタで決定したOpenMPスレッド制限を、子プロセス(tesseract)へ確実に継承させる。
            // ファイル単位並列と二重にならないようにするため（オーバーサブスクリプション回避）。
            // Ensure the child tesseract process inherits the OpenMP thread limit decided in the static
            // constructor, to avoid oversubscription with the tool's per-file parallelism.
            var ompThreadLimit = Environment.GetEnvironmentVariable("OMP_THREAD_LIMIT");
            if (!string.IsNullOrEmpty(ompThreadLimit))
            {
                processInfo.EnvironmentVariables["OMP_THREAD_LIMIT"] = ompThreadLimit;
            }

            using var process = System.Diagnostics.Process.Start(processInfo);
            process?.WaitForExit();

            string resultFilePath = tempOutputFile + ".txt";
            
            if (File.Exists(resultFilePath))
            {
                string text = File.ReadAllText(resultFilePath);
                File.Delete(resultFilePath); // 読み終わった一時ファイルを削除

                // 日本語（非ASCII文字）に隣接する空白（スペース・タブ）を除去する
                text = Regex.Replace(text, @"(?<=[^\x00-\x7F])[ \t　]+|[ \t　]+(?=[^\x00-\x7F])", "");
                
                return text;
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
    /// 画像パーツからOCRテキストを抽出する共通メソッドです。
    /// A common method to extract OCR text from an image part.
    /// </summary>
    private string ProcessImagePartOcr(DocumentFormat.OpenXml.Packaging.ImagePart imagePart, ref int imageCount)
    {
        var sb = new StringBuilder();
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
                
                // OCR実行 (このメソッド内でネイティブDLLエラー時のフォールバックが考慮されています)
                string ocrText = ReadImageTextWithOcr(newTempFilePath);
                
                // デバッグのため、エラーでも空でも強制的に出力する
                sb.AppendLine($"\n--- Embedded Image {imageCount} (ContentType: {imagePart.ContentType}) ---");
                sb.AppendLine(string.IsNullOrWhiteSpace(ocrText) ? "[No Text Found]" : ocrText);
                
                if (File.Exists(newTempFilePath)) File.Delete(newTempFilePath);
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"\n--- Embedded Image {imageCount} Error ---");
            sb.AppendLine(ex.Message);
        }
        
        imageCount++;
        return sb.ToString();
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

                // 埋め込み画像の存在チェックとOCR
                if (_settings.EnableOcr && wordDoc.MainDocumentPart?.ImageParts != null && wordDoc.MainDocumentPart.ImageParts.Any())
                {
                    sb.AppendLine("\n### [Embedded Images]");
                    int imageCount = 1;
                    foreach (var imagePart in wordDoc.MainDocumentPart.ImageParts)
                    {
                        // 共通のOCR処理メソッドを呼び出し
                        sb.Append(ProcessImagePartOcr(imagePart, ref imageCount));
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
                        int imageCount = 1; // プレゼンテーション全体で画像番号を連番にする

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

                                    // 埋め込み画像の存在チェックとOCR
                                    if (_settings.EnableOcr && slidePart.ImageParts != null && slidePart.ImageParts.Any())
                                    {
                                        foreach (var imagePart in slidePart.ImageParts)
                                        {
                                            // 共通のOCR処理メソッドを呼び出し
                                            sb.Append(ProcessImagePartOcr(imagePart, ref imageCount));
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

        // 【map】各ファイルのMarkdown生成をファイル単位で並列実行する（出力順序はインデックスで維持）。
        // [map] Generate the Markdown for each file in parallel (output order preserved by index).
        var markdownByIndex = GenerateMarkdownInParallel(allFiles);

        // 【reduce】生成結果を元の順序どおりに集約する（逐次処理）。
        // [reduce] Aggregate the generated results in original order (sequential).
        for (int i = 0; i < allFiles.Count; i++)
        {
            string fileMarkdown = markdownByIndex[i];
            if (string.IsNullOrEmpty(fileMarkdown)) continue;

            // 元の相対パスを取得し、末尾に .md を追加する（例: "src/Utils.cs" -> "src/Utils.cs.md"）
            // Get the original relative path and append .md to the end (e.g., "src/Utils.cs" -> "src/Utils.cs.md").
            string relativePath = Path.GetRelativePath(_settings.ProjectPath, allFiles[i]);
            string markdownRelativePath = relativePath + ".md";

            fileContents.Add((markdownRelativePath, fileMarkdown));
        }

        return fileContents;
    }

    /// <summary>
    /// ファイル一覧を受け取り、各ファイルのMarkdown生成をファイル単位で並列実行します。
    /// 結果は入力と同じインデックス位置に格納されるため、呼び出し側は出力順序を維持できます。
    /// Runs Markdown generation per file in parallel and returns the results at the same index
    /// positions as the input, so the caller can preserve the output order.
    /// </summary>
    /// <param name="allFiles">処理対象のファイルパス一覧 / The list of file paths to process.</param>
    /// <returns>各ファイルの生成結果（インデックス順） / The generated content per file, in index order.</returns>
    private string[] GenerateMarkdownInParallel(List<string> allFiles)
    {
        var results = new string[allFiles.Count];

        // 並列度は論理プロセッサ数に制限する。無制限だと画像ごとにTesseractEngineを大量生成し、
        // メモリやネイティブライブラリへの負荷が過大になるため。
        // Cap parallelism at the logical processor count. Unbounded parallelism would spawn many
        // TesseractEngine instances per image, overloading memory and the native library.
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
        };

        // GenerateMarkdownForFile は共有可変状態を持たない（_settings は読み取り専用、一時ファイルは一意名、
        // 例外は内部で捕捉）ため、ファイル単位の並列実行はスレッドセーフである。
        // GenerateMarkdownForFile has no shared mutable state (read-only _settings, unique temp file names,
        // exceptions caught internally), so per-file parallel execution is thread-safe.
        Parallel.For(0, allFiles.Count, parallelOptions, i =>
        {
            results[i] = GenerateMarkdownForFile(allFiles[i]);
        });

        return results;
    }
}