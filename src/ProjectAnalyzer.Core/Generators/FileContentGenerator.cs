using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ExcelDataReader;
using DocumentFormat.OpenXml;
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

    /// <summary>
    /// 内容を抽出せずスキップしたバイナリファイルのパス一覧です。ファイルの並び順は分析対象と同じです。
    /// The paths of the binary files skipped instead of having their content extracted, in the same
    /// order as the analyzed files.
    /// </summary>
    private readonly List<string> _skippedBinaryFiles = new List<string>();

    /// <summary>
    /// 直近の生成処理でスキップしたバイナリファイルのパス一覧を返します。
    /// 呼び出し側は、これらのファイルを原本のままコピーするなどの後処理に利用できます。
    /// Returns the paths of the binary files skipped during the most recent generation.
    /// The caller can use them for post-processing, such as copying the originals as-is.
    /// </summary>
    public IReadOnlyList<string> SkippedBinaryFiles => _skippedBinaryFiles;

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

        // 【reduce】生成済みの結果を元の順序どおりに連結・サイズ分割する（逐次処理）。
        // 各ファイルは既にしきい値以下のパートへ分割済みなので、ここではパート単位で詰めていく。
        // [reduce] Concatenate and split the pre-generated results in original order (sequential).
        // Each file is already split into parts under the threshold, so parts are packed here.
        var fileContents = new List<string>();
        var sb = new StringBuilder();
        sb.AppendLine("# \U0001f4c4 Project Context");
        sb.AppendLine();

        long currentSize = 0;

        // 見出しだけの空コンテキストを作らないよう、中身が入ったかどうかで判定する。
        // sb.Length では先頭の見出しに反応してしまい、最初のパートが単体でしきい値を超えた場合に
        // 見出しだけのファイルが生成されてしまう。
        // Track whether any content was added, so that an empty context is never emitted. Using
        // sb.Length would react to the leading heading and produce a heading-only file when the very
        // first part exceeds the threshold on its own.
        bool hasContent = false;

        foreach (var fileMarkdown in markdownByIndex.SelectMany(parts => parts))
        {
            // 処理をスキップしたファイル（画像や読み込みエラー等）は無視する
            // Ignore files that were skipped (e.g., images, read errors).
            if (string.IsNullOrEmpty(fileMarkdown)) continue;

            long fileSize = Encoding.UTF8.GetByteCount(fileMarkdown);

            if (hasContent && currentSize + fileSize > _settings.MaxOutputSize)
            {
                fileContents.Add(sb.ToString());
                sb.Clear();
                sb.AppendLine("# \U0001f4c4 Project Context (続き)");
                sb.AppendLine();
                currentSize = 0;
                hasContent = false;
            }

            sb.Append(fileMarkdown);
            currentSize += fileSize;
            hasContent = true;
        }

        // 対象ファイルが1つも無かった場合でも、従来どおり見出しだけのコンテキストを1つ返す
        // Even when there is no target file, return a single heading-only context as before.
        if (hasContent || fileContents.Count == 0)
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
    /// 単一のソースファイルから、しきい値以下になるよう分割されたMarkdownコンテンツを生成します。
    /// 分割はセクション（Excelのシート、PowerPointのスライド、Wordの見出し）の境界でのみ行い、
    /// セクションが1つしかないファイルや、セクションの途中で分割が必要になる場合は分割しません。
    /// Generates the Markdown content of a single source file, split into parts under the threshold.
    /// Splitting happens only at section boundaries (Excel sheets, PowerPoint slides, Word headings);
    /// files with a single section, or splits that would fall inside a section, are left unsplit.
    /// </summary>
    /// <param name="filePath">処理対象のソースファイルのパス。/ The path of the source file to process.</param>
    /// <returns>生成されたMarkdownコンテンツのパート一覧。/ The generated Markdown content parts.</returns>
    private List<string> GenerateMarkdownPartsForFile(string filePath)
    {
        try
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            List<string> sections;
            string language = "";

            // Excelファイルの場合の特別処理
            // Special handling for Excel files.
            if (IsExcelExtension(extension))
            {
                // 図形テキストと画像OCRはシート単位で取得し、各シートのセクション内へ差し込む。
                // 末尾へまとめると、セクション単位で分割したときにOCR結果が最後のチャンクへ偏るため。
                // Collect shape text and image OCR per sheet and place them inside each sheet's section.
                // Appending them at the end would skew all OCR results into the last chunk when the
                // output is split section by section.
                string? shapesError = null;
                var shapesBySheet = (extension == ".xlsx" || extension == ".xlsm")
                    ? ExtractExcelShapesAndImagesBySheet(filePath, out shapesError)
                    : new Dictionary<string, string>();

                sections = ReadExcelFile(filePath, shapesBySheet);

                if (shapesError != null)
                {
                    sections.Add($"[Excel Extract Error: {shapesError}]\n");
                }
            }
            // Wordファイル(.docx, .docm)の場合の特別処理
            // マクロ有効形式(.docm)も中身は同じOpen XMLのため、同じ経路で読み込む。
            // Special handling for Word files (.docx, .docm).
            // The macro-enabled format (.docm) is the same Open XML package, so it takes the same path.
            else if (IsWordExtension(extension))
            {
                sections = ReadWordFile(filePath);
            }
            // PowerPointファイル(.pptx)の場合の特別処理 (追加)
            // Special handling for PowerPoint files (.pptx).
            else if (IsPowerPointExtension(extension))
            {
                sections = ReadPowerPointFile(filePath);
            }
            else
            {
                // 通常のテキストファイルは構造を推測できないため、常に単一セクションとして扱う。
                // ソースコード中の "### コメント" を見出しと誤検出しないよう、分割対象から外している。
                // Plain text files have no structure we can infer, so they are always a single section.
                // This keeps "### comment" lines in source code from being mistaken for headings.
                sections = new List<string> { File.ReadAllText(filePath) };
                language = LanguageMapper.GetLanguage(extension);
            }

            // 内容が空のセクションは出力しない。すべて空ならファイルごとスキップする。
            // Drop empty sections, and skip the whole file when nothing remains.
            sections = sections.Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (sections.Count == 0) return new List<string>();

            for (int i = 0; i < sections.Count; i++)
            {
                sections[i] = ApplyContentOptions(sections[i]);
            }

            return RenderMarkdownParts(filePath, sections, language);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [Warning] Could not process file '{Path.GetFileName(filePath)}': {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Excelとして読み込む拡張子かどうかを判定します。
    /// Determines whether the extension is read as an Excel file.
    /// </summary>
    private static bool IsExcelExtension(string extension)
        => extension is ".xlsx" or ".xls" or ".xlsm";

    /// <summary>
    /// Wordとして読み込む拡張子かどうかを判定します。
    /// Determines whether the extension is read as a Word file.
    /// </summary>
    private static bool IsWordExtension(string extension)
        => extension is ".docx" or ".docm";

    /// <summary>
    /// PowerPointとして読み込む拡張子かどうかを判定します。
    /// Determines whether the extension is read as a PowerPoint file.
    /// </summary>
    private static bool IsPowerPointExtension(string extension)
        => extension is ".pptx";

    /// <summary>
    /// 専用の抽出処理を持つファイルかどうかを判定します。
    /// これらはZIPやOLE形式のためバイナリと判定されてしまうので、バイナリ判定より先に振り分けます。
    /// Determines whether a file has a dedicated extraction path.
    /// Such files are ZIP or OLE containers and would be flagged as binary, so they are routed
    /// before the binary check runs.
    /// </summary>
    /// <param name="filePath">判定対象のファイルパス / The path of the file to inspect.</param>
    /// <returns>専用の抽出処理を持つ場合は true / true when a dedicated extractor exists.</returns>
    private static bool HasDedicatedExtractor(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return IsExcelExtension(extension) || IsWordExtension(extension) || IsPowerPointExtension(extension);
    }

    /// <summary>
    /// 出力オプション（HTMLタグの無害化・インデント削除）をコンテンツへ適用します。
    /// Applies the output options (HTML sanitization and indent removal) to the content.
    /// </summary>
    /// <param name="content">対象のコンテンツ / The content to transform.</param>
    /// <returns>オプション適用後のコンテンツ / The transformed content.</returns>
    private string ApplyContentOptions(string content)
    {
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

        return content;
    }

    /// <summary>
    /// セクション群をしきい値以下のパートへまとめ、各パートをMarkdownとして描画します。
    /// 分割されるのは「しきい値を超える」かつ「セクションが2つ以上ある」場合のみです。
    /// Packs the sections into parts under the threshold and renders each part as Markdown.
    /// A file is split only when it exceeds the threshold *and* has more than one section.
    /// </summary>
    /// <param name="filePath">対象ファイルのパス / The path of the target file.</param>
    /// <param name="sections">分割単位となるセクション群 / The sections that serve as split boundaries.</param>
    /// <param name="language">コードブロックに付与する言語名 / The language name for the code block.</param>
    /// <returns>描画されたパート一覧 / The rendered parts.</returns>
    private List<string> RenderMarkdownParts(string filePath, List<string> sections, string language)
    {
        var packedParts = PackSections(sections, language);
        var renderedParts = new List<string>(packedParts.Count);

        for (int i = 0; i < packedParts.Count; i++)
        {
            // パートが1つだけのときは連番を付けず、従来と同じ見出しにする
            // Keep the original heading when there is only one part.
            string? partLabel = packedParts.Count > 1 ? $"{i + 1}/{packedParts.Count}" : null;
            renderedParts.Add(RenderPart(filePath, packedParts[i], language, partLabel));
        }

        return renderedParts;
    }

    /// <summary>
    /// セクション群を、レンダリング後のサイズがしきい値へ収まるようパートへまとめます。
    /// 1つのセクションだけでしきい値を超える場合は、セクションの途中で切らずに超過させます。
    /// Packs the sections into parts so that each rendered part fits within the threshold.
    /// A section that exceeds the threshold on its own is left oversized rather than cut in half.
    /// </summary>
    /// <param name="sections">分割単位となるセクション群 / The sections that serve as split boundaries.</param>
    /// <param name="language">コードブロックに付与する言語名 / The language name for the code block.</param>
    /// <returns>パートごとにまとめられたセクション群 / The sections grouped per part.</returns>
    private List<List<string>> PackSections(List<string> sections, string language)
    {
        // セクションが1つしかなければ分割の余地がないため、そのまま1パートとする
        // A single section leaves nothing to split, so it becomes one part as-is.
        if (sections.Count <= 1) return new List<List<string>> { sections };

        // しきい値と比較するのは、共通ヘッダやdetails・コードブロックまで含めたレンダリング後のサイズ。
        // コードブロックを出力する場合は本文が2回出力されるため、セクションのコストも2倍になる。
        // The threshold is compared against the rendered size, including the header, the details block
        // and the code block. When the code block is emitted the content appears twice, so each
        // section costs twice as much.
        long overhead = Encoding.UTF8.GetByteCount(RenderPart("dummy", new List<string>(), language, $"{sections.Count}/{sections.Count}"));
        int contentCopies = _settings.OmitCodeBlockTicks ? 1 : 2;

        var parts = new List<List<string>>();
        var current = new List<string>();
        long currentSize = 0;

        foreach (var section in sections)
        {
            long sectionSize = Encoding.UTF8.GetByteCount(section) * contentCopies;

            if (current.Count > 0 && overhead + currentSize + sectionSize > _settings.MaxOutputSize)
            {
                parts.Add(current);
                current = new List<string>();
                currentSize = 0;
            }

            current.Add(section);
            currentSize += sectionSize;
        }

        if (current.Count > 0) parts.Add(current);

        return parts;
    }

    /// <summary>
    /// 1つのパートをMarkdownとして描画します。分割時もファイル名と相対パスを共通ヘッダとして再掲し、
    /// details とコードブロックはパート内で必ず開いて閉じます。
    /// Renders a single part as Markdown. The file name and relative path are repeated as a shared
    /// header even when split, and the details block and code block are always opened and closed
    /// within the same part.
    /// </summary>
    /// <param name="filePath">対象ファイルのパス / The path of the target file.</param>
    /// <param name="sections">このパートへ含めるセクション群 / The sections to include in this part.</param>
    /// <param name="language">コードブロックに付与する言語名 / The language name for the code block.</param>
    /// <param name="partLabel">"2/3" 形式のパート表記。分割されていない場合は null / The "2/3" style part label, or null when not split.</param>
    /// <returns>描画されたMarkdown / The rendered Markdown.</returns>
    private string RenderPart(string filePath, List<string> sections, string language, string? partLabel)
    {
        string relativePath = Path.GetRelativePath(_settings.ProjectPath, filePath);
        string content = string.Join(Environment.NewLine, sections);

        var sb = new StringBuilder();

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine(partLabel == null
            ? $"## {Path.GetFileName(filePath)}"
            : $"## {Path.GetFileName(filePath)} ({partLabel})");
        sb.AppendLine();
        sb.AppendLine($"**Relative Path:** `{relativePath}`");
        sb.AppendLine();

        if (partLabel != null)
        {
            sb.AppendLine($"**Part:** {partLabel}");
            sb.AppendLine();
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


    /// <summary>
    /// Excelファイルを読み込み、シート単位のセクションに分けたマークダウンとして返します。
    /// シートごとの図形・画像OCRのテキストを受け取り、対応するシートのセクション内へ出力します。
    /// Reads an Excel file and returns it as Markdown split into one section per sheet.
    /// Takes the per-sheet shape and image OCR text and emits it inside the matching sheet's section.
    /// </summary>
    /// <param name="filePath">読み込むExcelファイルのパス / The path of the Excel file to read.</param>
    /// <param name="shapesBySheet">シート名をキーとした図形・画像OCRのテキスト / Shape and image OCR text keyed by sheet name.</param>
    /// <returns>シートごとのセクション一覧 / The list of sections, one per sheet.</returns>
   private List<string> ReadExcelFile(string filePath, IReadOnlyDictionary<string, string> shapesBySheet)
    {
        var sections = new List<string>();

        // どのシートにも差し込めなかったぶんを検出するため、消化済みのシート名を記録する
        // Track consumed sheet names so that leftovers can be detected afterwards.
        var consumedSheets = new HashSet<string>();

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
                    // シートごとに独立したセクションとして組み立てる（容量分割の単位になる）
                    // Build each sheet as its own section, which is the unit used for size splitting.
                    var sb = new StringBuilder();

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

                    // このシートに属する図形・画像OCRを、シートのセクション内に続けて出力する
                    // Emit the shapes and image OCR belonging to this sheet within the sheet's section.
                    if (shapesBySheet.TryGetValue(table.TableName, out var sheetShapes)
                        && !string.IsNullOrWhiteSpace(sheetShapes))
                    {
                        consumedSheets.Add(table.TableName);
                        sb.AppendLine();
                        sb.AppendLine("#### [Shapes, TextBoxes & Images]");
                        sb.AppendLine(sheetShapes.TrimEnd());
                    }

                    sections.Add(sb.ToString());
                }
            }
        }

        // シート名が一致せず差し込めなかったぶんは、内容を失わないよう末尾へ独立したセクションとして出力する
        // Emit anything that could not be matched to a sheet as its own trailing section, so that no
        // content is lost.
        foreach (var pair in shapesBySheet)
        {
            if (consumedSheets.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;

            var leftover = new StringBuilder();
            leftover.AppendLine($"### [Shapes, TextBoxes & Images] ({pair.Key})");
            leftover.AppendLine(pair.Value.TrimEnd());
            sections.Add(leftover.ToString());
        }

        return sections;
    }

    /// <summary>
    /// Excelファイル(.xlsx, .xlsm)から図形やテキストボックスの文字、および埋め込み画像のOCRテキストを、
    /// シート単位に分けて抽出します。呼び出し側が各シートのセクション内へ差し込めるようにするためです。
    /// Extracts shape and text box text plus embedded image OCR text from an Excel file (.xlsx, .xlsm),
    /// grouped per sheet so that the caller can place each group inside the matching sheet's section.
    /// </summary>
    /// <param name="filePath">読み込むExcelファイルのパス / The path of the Excel file to read.</param>
    /// <param name="error">抽出中に発生したエラーのメッセージ（正常時は null） / The error message if extraction failed, otherwise null.</param>
    /// <returns>シート名をキーとした抽出テキスト / The extracted text keyed by sheet name.</returns>
    private Dictionary<string, string> ExtractExcelShapesAndImagesBySheet(string filePath, out string? error)
    {
        error = null;
        var textBySheet = new Dictionary<string, string>();

        try
        {
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(filePath, false))
            {
                var workbookPart = doc.WorkbookPart;
                if (workbookPart?.Workbook?.Sheets == null) return textBySheet;

                // 画像番号はブック全体で通し番号にする
                // Number the images sequentially across the whole workbook.
                int imageCount = 1;

                // WorksheetPart を直接辿るとシート名が得られないため、ブックのシート定義から辿る。
                // 定義順に処理することで、出力順序もブック上の並びと一致する。
                // Iterating WorksheetParts directly does not give sheet names, so walk the workbook's
                // sheet definitions instead. Their order also matches the sheet order in the workbook.
                foreach (var sheet in workbookPart.Workbook.Sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>())
                {
                    string? sheetName = sheet.Name?.Value;
                    string? relationshipId = sheet.Id?.Value;
                    if (string.IsNullOrEmpty(sheetName) || string.IsNullOrEmpty(relationshipId)) continue;

                    if (workbookPart.GetPartById(relationshipId) is not WorksheetPart sheetPart) continue;
                    if (sheetPart.DrawingsPart == null) continue;

                    var sb = new StringBuilder();

                    // 1. 図形やテキストボックス内の文字データを抽出
                    // 1. Extract the text inside shapes and text boxes.
                    var worksheetDrawing = sheetPart.DrawingsPart.WorksheetDrawing;
                    if (worksheetDrawing != null)
                    {
                        foreach (var text in worksheetDrawing.Descendants<DocumentFormat.OpenXml.Drawing.Text>())
                        {
                            if (!string.IsNullOrWhiteSpace(text.Text)) sb.AppendLine(text.Text);
                        }
                    }

                    // 2. 埋め込み画像の存在チェックとOCR
                    // 2. Check for embedded images and run OCR.
                    if (sheetPart.DrawingsPart.ImageParts != null && sheetPart.DrawingsPart.ImageParts.Any())
                    {
                        if (!_settings.EnableOcr)
                        {
                            sb.AppendLine($"\n--- ⚠️ 画像が見つかりましたが、OCRが無効(--enable-ocrなし)のためスキップしました ---");
                        }
                        else
                        {
                            foreach (var imagePart in sheetPart.DrawingsPart.ImageParts)
                            {
                                // 共通のOCR処理メソッドを呼び出し
                                // Call the shared OCR processing method.
                                sb.Append(ProcessImagePartOcr(imagePart, ref imageCount));
                            }
                        }
                    }

                    // 同名シートは存在しえないが、破損ファイル等での上書きを避けるため追記扱いにする
                    // Duplicate sheet names cannot exist, but append rather than overwrite to be safe
                    // against malformed files.
                    if (sb.Length > 0)
                    {
                        textBySheet[sheetName!] = textBySheet.TryGetValue(sheetName!, out var existing)
                            ? existing + sb.ToString()
                            : sb.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return textBySheet;
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
    /// Wordファイル(.docx)を読み込み、見出し(H1)単位のセクションに分けて返します。
    /// 見出しスタイルが使われていない文書は、分割できる境界が無いため単一のセクションになります。
    /// Reads a Word file (.docx) and returns it split into one section per top-level (H1) heading.
    /// A document that does not use heading styles has no split boundary and yields a single section.
    /// </summary>
    /// <param name="filePath">読み込むWordファイルのパス / The path of the Word file to read.</param>
    /// <returns>見出し単位のセクション一覧 / The list of sections, one per top-level heading.</returns>
    private List<string> ReadWordFile(string filePath)
    {
        var sectionBuilder = new WordSectionBuilder();
        try
        {
            using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(filePath, false))
            {
                var mainPart = wordDoc.MainDocumentPart;

                // 画像OCRは、画像が実際に置かれている段落・表の直後へ出力する。
                // 末尾へまとめると、セクション単位で分割したときにOCR結果が最後のチャンクへ偏るため。
                // Emit image OCR right after the paragraph or table where the image actually appears.
                // Appending it at the end would skew all OCR results into the last chunk when the
                // output is split section by section.
                var imageContext = (_settings.EnableOcr && mainPart != null)
                    ? new WordImageContext(mainPart)
                    : null;

                // 見出しスタイルの判定表をスタイル定義から作る（styleId の文字列比較では判定できないため）
                // Build the heading style lookup from the style definitions, because comparing the
                // styleId as a string is not reliable.
                var headingLevels = mainPart != null
                    ? BuildWordHeadingLevelsByStyleId(mainPart)
                    : new Dictionary<string, int>(StringComparer.Ordinal);

                // 入れ子の表の通し番号は文書ごとに振る。ファイル単位で並列実行されるため、
                // インスタンスの状態にはせず、この文書の走査だけで共有する。
                // Nested tables are numbered per document. Files are processed in parallel, so the
                // counter is not instance state; it is shared only within this document's walk.
                var nestedTableNumbering = new WordNestedTableNumbering();

                var body = mainPart?.Document?.Body;
                if (body != null)
                {
                    // 本文のブロック要素(段落・表)を出現順に抽出する
                    // Extract the block elements (paragraphs and tables) of the body in document order.
                    AppendWordBlocks(body, sectionBuilder, imageContext, headingLevels, nestedTableNumbering);
                }

                // 本文から参照されていない画像（差し込み位置を特定できないもの）は、
                // 取りこぼさないよう従来どおり末尾へまとめて出力する。
                // Images not referenced from the body (whose position cannot be determined) are still
                // emitted together at the end, as before, so that nothing is lost.
                if (imageContext != null)
                {
                    var unreferenced = new StringBuilder();
                    foreach (var imagePart in imageContext.MainPart.ImageParts)
                    {
                        if (!imageContext.MarkProcessed(imagePart)) continue;

                        // 共通のOCR処理メソッドを呼び出し
                        // Call the shared OCR processing method.
                        unreferenced.Append(ProcessImagePartOcr(imagePart, ref imageContext.ImageCount));
                    }

                    if (unreferenced.Length > 0)
                    {
                        sectionBuilder.Current.AppendLine("\n### [Embedded Images]");
                        sectionBuilder.Current.Append(unreferenced);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   [Warning] Could not read Word file '{Path.GetFileName(filePath)}': {ex.Message}");
            return new List<string>();
        }

        return sectionBuilder.ToSections();
    }

    /// <summary>
    /// スタイル定義から、スタイルIDと見出しレベル(1始まり)の対応表を作ります。
    /// 日本語版Wordでは組み込み見出しのスタイルIDが "a3" のような自動生成値になることがあるため、
    /// スタイルIDの文字列比較ではなく、スタイル定義の正規名("heading 1")とアウトラインレベルで判定します。
    /// Builds a lookup from style ID to heading level (1-based) using the style definitions.
    /// Japanese Word can assign auto-generated style IDs such as "a3" to the built-in heading styles,
    /// so the canonical style name ("heading 1") and the outline level are used instead of the ID.
    /// </summary>
    /// <param name="mainPart">対象のメインドキュメントパート / The main document part to inspect.</param>
    /// <returns>スタイルIDをキーとした見出しレベルの対応表 / The heading levels keyed by style ID.</returns>
    private static Dictionary<string, int> BuildWordHeadingLevelsByStyleId(MainDocumentPart mainPart)
    {
        var headingLevels = new Dictionary<string, int>(StringComparer.Ordinal);

        var styles = mainPart.StyleDefinitionsPart?.Styles;
        if (styles == null) return headingLevels;

        foreach (var style in styles.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>())
        {
            string? styleId = style.StyleId?.Value;
            if (string.IsNullOrEmpty(styleId)) continue;

            int? level = ParseHeadingLevelFromName(style.StyleName?.Val?.Value)
                ?? ToHeadingLevel(style.StyleParagraphProperties?.OutlineLevel?.Val?.Value);

            if (level != null) headingLevels[styleId!] = level.Value;
        }

        return headingLevels;
    }

    /// <summary>
    /// 段落が見出しかどうかを判定し、見出しレベル(1始まり)を返します。見出しでない場合は null を返します。
    /// Determines whether a paragraph is a heading and returns its level (1-based), or null otherwise.
    /// </summary>
    /// <param name="paragraph">判定対象の段落 / The paragraph to inspect.</param>
    /// <param name="headingLevels">スタイルIDをキーとした見出しレベルの対応表 / The heading levels keyed by style ID.</param>
    /// <returns>見出しレベル、または null / The heading level, or null.</returns>
    private static int? GetWordHeadingLevel(DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph, Dictionary<string, int> headingLevels)
    {
        var properties = paragraph.ParagraphProperties;
        string? styleId = properties?.ParagraphStyleId?.Val?.Value;

        if (!string.IsNullOrEmpty(styleId))
        {
            if (headingLevels.TryGetValue(styleId!, out int level)) return level;

            // スタイル定義を持たない文書向けのフォールバック（styleId が "Heading1" 等の場合）
            // Fallback for documents without style definitions (e.g. a styleId of "Heading1").
            int? levelFromId = ParseHeadingLevelFromName(styleId);
            if (levelFromId != null) return levelFromId;
        }

        // 段落自身にアウトラインレベルが指定されている場合
        // When the paragraph itself carries an outline level.
        return ToHeadingLevel(properties?.OutlineLevel?.Val?.Value);
    }

    /// <summary>
    /// スタイル名("heading 1", "Heading1" など)から見出しレベルを取り出します。
    /// Extracts the heading level from a style name such as "heading 1" or "Heading1".
    /// </summary>
    private static int? ParseHeadingLevelFromName(string? styleName)
    {
        if (string.IsNullOrEmpty(styleName)) return null;

        var match = Regex.Match(styleName!, @"^heading\s*([1-9])$", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
    }

    /// <summary>
    /// アウトラインレベル(0始まり)を見出しレベル(1始まり)へ変換します。
    /// 本文を表す値(9)や未設定の場合は null を返します。
    /// Converts an outline level (0-based) into a heading level (1-based).
    /// Returns null when the value represents body text (9) or is not set.
    /// </summary>
    private static int? ToHeadingLevel(int? outlineLevel)
        => outlineLevel is >= 0 and <= 8 ? outlineLevel + 1 : null;

    /// <summary>
    /// Word文書のセクション（H1見出し単位）を組み立てるための状態を保持します。
    /// Holds the state used to build a Word document's sections, one per top-level (H1) heading.
    /// </summary>
    private sealed class WordSectionBuilder
    {
        private readonly List<string> _sections = new List<string>();
        private StringBuilder _current = new StringBuilder();

        /// <summary>
        /// 現在組み立て中のセクションの出力先 / The destination of the section currently being built.
        /// </summary>
        public StringBuilder Current => _current;

        /// <summary>
        /// 新しいセクションを開始します。H1見出しを出力する直前に呼び出します。
        /// 最初の見出しより前の本文（前書きなど）は、独立した先頭のセクションになります。
        /// Starts a new section, called just before emitting a top-level heading. Any body text before
        /// the first heading (such as a preamble) becomes its own leading section.
        /// </summary>
        public void StartNewSection()
        {
            if (_current.Length == 0) return;

            _sections.Add(_current.ToString());
            _current = new StringBuilder();
        }

        /// <summary>
        /// 組み立て済みのセクション一覧を返します。
        /// Returns the sections that have been built.
        /// </summary>
        public List<string> ToSections()
        {
            if (_current.Length > 0) _sections.Add(_current.ToString());
            return _sections;
        }
    }

    /// <summary>
    /// Word文書のブロック要素(段落・表)を、出現順にMarkdownとして追記します。
    /// 表はセル単位の構造を保つため、Markdownのテーブル記法へ変換します。
    /// Appends the block elements (paragraphs and tables) of a Word document as Markdown, in document
    /// order. Tables are converted to Markdown table syntax so that their cell structure is preserved.
    /// </summary>
    /// <param name="container">走査対象の要素（本文やコンテンツコントロール等） / The element to walk (body, content control, etc.).</param>
    /// <param name="sectionBuilder">セクションの組み立て先 / The section builder to write into.</param>
    /// <param name="imageContext">画像OCRの処理状態。OCRが無効な場合は null / The image OCR state, or null when OCR is disabled.</param>
    /// <param name="headingLevels">スタイルIDをキーとした見出しレベルの対応表 / The heading levels keyed by style ID.</param>
    /// <param name="nestedTableNumbering">入れ子の表へ通し番号を振るカウンタ / The counter numbering nested tables.</param>
    private void AppendWordBlocks(OpenXmlElement container, WordSectionBuilder sectionBuilder, WordImageContext? imageContext, Dictionary<string, int> headingLevels, WordNestedTableNumbering nestedTableNumbering)
    {
        foreach (var element in container.Elements())
        {
            switch (element)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph:
                    AppendWordParagraph(paragraph, sectionBuilder, headingLevels);

                    // テキストボックスや図形の中身は段落の内側に入っているため、ブロック要素として辿り直す。
                    // ここで再帰しないと、中の表が段落テキストの一部として1行に潰れてしまう。
                    // Text box and shape content lives inside the paragraph, so walk it as block elements.
                    // Without this recursion a table inside a text box collapses into one line of text.
                    foreach (var textBox in GetWordTextBoxContents(paragraph))
                    {
                        AppendWordBlocks(textBox, sectionBuilder, imageContext, headingLevels, nestedTableNumbering);
                    }

                    // 画像は段落内に配置されるため、その段落の直後へOCR結果を出力する
                    // Images live inside paragraphs, so emit their OCR right after that paragraph.
                    sectionBuilder.Current.Append(ExtractWordImagesOcr(paragraph, imageContext));
                    break;

                case DocumentFormat.OpenXml.Wordprocessing.Table table:
                    AppendWordTable(table, sectionBuilder.Current, nestedTableNumbering);
                    // 表の途中に差し込むとMarkdownのテーブルが崩れるため、表の直後へまとめて出力する
                    // Inserting inside the table would break the Markdown table, so emit it right after.
                    sectionBuilder.Current.Append(ExtractWordImagesOcr(table, imageContext));
                    break;

                case AlternateContent alternateContent:
                    // 同じ内容が mc:Choice(新形式) と mc:Fallback(旧形式) の両方に入っているため、
                    // 片方だけを採用する。両方辿ると同じ表やテキストが二重に出力される。
                    // The same content is stored in both mc:Choice (modern) and mc:Fallback (legacy),
                    // so only one branch is taken. Walking both would emit tables and text twice.
                    var chosenBranch = ChooseAlternateContentBranch(alternateContent);
                    if (chosenBranch != null)
                    {
                        AppendWordBlocks(chosenBranch, sectionBuilder, imageContext, headingLevels, nestedTableNumbering);
                    }
                    break;

                default:
                    // コンテンツコントロール(SdtBlock)など、段落や表を内包しうる要素は再帰的に辿る。
                    // 表の中身は上のcaseで処理済みのため、ここで二重に出力されることはない。
                    // Recurse into elements that may contain paragraphs or tables (e.g. SdtBlock).
                    // Table contents are handled in the case above, so nothing is emitted twice.
                    if (element.HasChildren)
                    {
                        AppendWordBlocks(element, sectionBuilder, imageContext, headingLevels, nestedTableNumbering);
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Wordの段落を追記します。見出しスタイルの段落はMarkdownの見出しへ変換し、
    /// 最上位(H1)の見出しでは容量分割の境界となる新しいセクションを開始します。
    /// Appends a Word paragraph. Paragraphs styled as headings are converted into Markdown headings,
    /// and a top-level (H1) heading starts a new section, which is the boundary used for size splitting.
    /// </summary>
    /// <param name="paragraph">対象の段落 / The paragraph to append.</param>
    /// <param name="sectionBuilder">セクションの組み立て先 / The section builder to write into.</param>
    /// <param name="headingLevels">スタイルIDをキーとした見出しレベルの対応表 / The heading levels keyed by style ID.</param>
    private static void AppendWordParagraph(DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph, WordSectionBuilder sectionBuilder, Dictionary<string, int> headingLevels)
    {
        string text = GetWordParagraphText(paragraph);

        // 文字の無い段落は見出しとして扱わない（空の見出しでセクションを切らないため）
        // A paragraph without text is not treated as a heading, so empty headings do not split sections.
        int? headingLevel = string.IsNullOrWhiteSpace(text) ? null : GetWordHeadingLevel(paragraph, headingLevels);

        if (headingLevel == null)
        {
            sectionBuilder.Current.AppendLine(text);
            return;
        }

        // H1のみを分割の境界とする（H2以降で細切れにしない）
        // Only H1 acts as a split boundary, so deeper headings do not fragment the output.
        if (headingLevel == 1) sectionBuilder.StartNewSection();

        // "##" はファイル名の見出しに使っているため、WordのH1は "###" から始める
        // "##" is used for the file name heading, so a Word H1 starts at "###".
        string hashes = new string('#', Math.Min(headingLevel.Value + 2, 6));
        sectionBuilder.Current.AppendLine($"{hashes} {text}");
    }

    /// <summary>
    /// 段落自身のテキストを取得します。InnerText と違い、テキストボックスの中身は含めず、
    /// mc:AlternateContent は採用するブランチの分だけを拾います。
    /// テキストボックスの中身は呼び出し側がブロック要素として別途出力するため、ここで含めると
    /// 表が1行に潰れたうえ、新旧2つのブランチぶん二重に出力されてしまいます。
    /// Gets the text of the paragraph itself. Unlike InnerText it excludes text box content and reads
    /// only the chosen branch of an mc:AlternateContent. The caller emits text box content separately
    /// as block elements; including it here would flatten tables into one line and emit everything
    /// twice, once for each of the modern and legacy branches.
    /// </summary>
    /// <param name="paragraph">対象の段落 / The paragraph to read.</param>
    /// <returns>段落のテキスト / The text of the paragraph.</returns>
    private static string GetWordParagraphText(DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph)
    {
        var sb = new StringBuilder();
        AppendWordTextExcludingTextBoxes(paragraph, sb);
        return sb.ToString();
    }

    /// <summary>
    /// 要素配下のテキストを、テキストボックスの中身を除いて連結します。
    /// mc:AlternateContent に出会った場合は、採用するブランチだけを辿ります。
    /// Concatenates the text under an element, excluding text box content.
    /// When an mc:AlternateContent is encountered, only the chosen branch is walked.
    /// </summary>
    /// <param name="element">走査対象の要素 / The element to walk.</param>
    /// <param name="sb">出力先のStringBuilder / The destination StringBuilder.</param>
    private static void AppendWordTextExcludingTextBoxes(OpenXmlElement element, StringBuilder sb)
    {
        foreach (var child in element.Elements())
        {
            switch (child)
            {
                // テキストボックスの中身はブロック要素として別途出力するため、ここでは拾わない
                // Text box content is emitted separately as block elements, so it is skipped here.
                case DocumentFormat.OpenXml.Wordprocessing.TextBoxContent:
                    break;

                case AlternateContent alternateContent:
                    var chosenBranch = ChooseAlternateContentBranch(alternateContent);
                    if (chosenBranch != null) AppendWordTextExcludingTextBoxes(chosenBranch, sb);
                    break;

                default:
                    // 子を持たない要素(w:t など)が実際のテキストを保持している
                    // Elements without children (such as w:t) are the ones holding the actual text.
                    if (child.HasChildren) AppendWordTextExcludingTextBoxes(child, sb);
                    else sb.Append(child.InnerText);
                    break;
            }
        }
    }

    /// <summary>
    /// 要素配下にあるテキストボックスの中身を、出現順に列挙します。
    /// mc:AlternateContent は採用するブランチだけを辿るため、同じテキストボックスが
    /// 新旧2つの形式で二重に返ることはありません。
    /// Enumerates the text box content under an element, in document order.
    /// Only the chosen branch of an mc:AlternateContent is walked, so the same text box is never
    /// returned twice through its modern and legacy representations.
    /// </summary>
    /// <param name="element">走査対象の要素 / The element to walk.</param>
    /// <returns>テキストボックスの中身の列挙 / The enumeration of text box content elements.</returns>
    private static IEnumerable<DocumentFormat.OpenXml.Wordprocessing.TextBoxContent> GetWordTextBoxContents(OpenXmlElement element)
    {
        foreach (var child in element.Elements())
        {
            switch (child)
            {
                case DocumentFormat.OpenXml.Wordprocessing.TextBoxContent textBox:
                    // 入れ子のテキストボックスは、この中身をブロックとして辿るときに拾われる
                    // A nested text box is picked up when this content is walked as block elements.
                    yield return textBox;
                    break;

                case AlternateContent alternateContent:
                    var chosenBranch = ChooseAlternateContentBranch(alternateContent);
                    if (chosenBranch == null) break;
                    foreach (var textBox in GetWordTextBoxContents(chosenBranch)) yield return textBox;
                    break;

                default:
                    foreach (var textBox in GetWordTextBoxContents(child)) yield return textBox;
                    break;
            }
        }
    }

    /// <summary>
    /// mc:AlternateContent のうち、内容として採用するブランチを返します。
    /// 新しい形式(mc:Choice)を優先し、無ければ旧形式(mc:Fallback)を使います。
    /// Requires 属性の対応可否までは判定できないため、複数ある場合は先頭の mc:Choice を採用します。
    /// Returns the branch of an mc:AlternateContent whose content should be used.
    /// The modern form (mc:Choice) wins, falling back to the legacy form (mc:Fallback).
    /// The Requires attribute cannot be evaluated here, so the first mc:Choice is taken.
    /// </summary>
    /// <param name="alternateContent">対象の mc:AlternateContent / The mc:AlternateContent to inspect.</param>
    /// <returns>採用するブランチ、または null / The branch to use, or null.</returns>
    private static OpenXmlElement? ChooseAlternateContentBranch(AlternateContent alternateContent)
        => (OpenXmlElement?)alternateContent.GetFirstChild<AlternateContentChoice>()
           ?? alternateContent.GetFirstChild<AlternateContentFallback>();

    /// <summary>
    /// mc:AlternateContent の採用しないブランチ（通常は mc:Fallback）に属する要素かどうかを判定します。
    /// 採用しないブランチの内容を出力に含めると、同じ内容が二重に出力されてしまいます。
    /// Determines whether an element belongs to the discarded branch of an mc:AlternateContent
    /// (normally mc:Fallback). Including a discarded branch would emit the same content twice.
    /// </summary>
    /// <param name="element">判定対象の要素 / The element to inspect.</param>
    /// <returns>採用しないブランチに属する場合は true / true when the element is in a discarded branch.</returns>
    private static bool IsInDiscardedAlternateContentBranch(OpenXmlElement element)
    {
        for (var ancestor = element.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor.Parent is not AlternateContent alternateContent) continue;
            if (ancestor is not AlternateContentChoice && ancestor is not AlternateContentFallback) continue;

            if (!ReferenceEquals(ChooseAlternateContentBranch(alternateContent), ancestor)) return true;
        }

        return false;
    }

    /// <summary>
    /// 指定要素の中から参照されている埋め込み画像をOCRし、Markdownとして返します。
    /// 一度処理した画像は再処理しないため、同じ画像が複数回参照されていても出力は1回だけです。
    /// Runs OCR on the embedded images referenced within the given element and returns it as Markdown.
    /// Each image is processed only once, so an image referenced multiple times is emitted only once.
    /// </summary>
    /// <param name="container">走査対象の要素（段落や表） / The element to walk (a paragraph or a table).</param>
    /// <param name="imageContext">画像OCRの処理状態。OCRが無効な場合は null / The image OCR state, or null when OCR is disabled.</param>
    /// <returns>生成されたOCR結果のMarkdown / The generated OCR result as Markdown.</returns>
    private string ExtractWordImagesOcr(OpenXmlElement container, WordImageContext? imageContext)
    {
        if (imageContext == null) return string.Empty;

        var sb = new StringBuilder();

        foreach (string relationshipId in GetWordImageRelationshipIds(container))
        {
            if (!imageContext.TryGetImagePart(relationshipId, out var imagePart)) continue;
            if (!imageContext.MarkProcessed(imagePart)) continue;

            // 共通のOCR処理メソッドを呼び出し
            // Call the shared OCR processing method.
            sb.Append(ProcessImagePartOcr(imagePart, ref imageContext.ImageCount));
        }

        return sb.ToString();
    }

    /// <summary>
    /// 要素の配下から、画像への参照ID(リレーションシップID)を出現順に列挙します。
    /// 現行形式(DrawingMLの a:blip)と旧形式(VMLの v:imagedata)の双方に対応します。
    /// Enumerates the relationship IDs referencing images under an element, in document order.
    /// Both the current format (DrawingML a:blip) and the legacy format (VML v:imagedata) are handled.
    /// </summary>
    /// <param name="container">走査対象の要素 / The element to walk.</param>
    /// <returns>画像への参照IDの列挙 / The enumeration of relationship IDs referencing images.</returns>
    private static IEnumerable<string> GetWordImageRelationshipIds(OpenXmlElement container)
    {
        foreach (var element in container.Descendants())
        {
            switch (element)
            {
                case DocumentFormat.OpenXml.Drawing.Blip blip when !string.IsNullOrEmpty(blip.Embed?.Value):
                    yield return blip.Embed!.Value!;
                    break;

                case DocumentFormat.OpenXml.Vml.ImageData imageData when !string.IsNullOrEmpty(imageData.RelationshipId?.Value):
                    yield return imageData.RelationshipId!.Value!;
                    break;
            }
        }
    }

    /// <summary>
    /// Word文書の埋め込み画像を出現位置ごとに処理するための状態を保持します。
    /// Holds the state needed to process a Word document's embedded images at their positions.
    /// </summary>
    private sealed class WordImageContext
    {
        /// <summary>
        /// 文書全体で通し番号にするための画像カウンタ。
        /// `ref` 引数として渡すためプロパティではなくフィールドにしています。
        /// The image counter, kept sequential across the document.
        /// It is a field rather than a property so that it can be passed as a `ref` argument.
        /// </summary>
        public int ImageCount = 1;

        /// <summary>
        /// 本文が属するメインドキュメントパート / The main document part that owns the body.
        /// </summary>
        public MainDocumentPart MainPart { get; }

        /// <summary>
        /// 参照ID(r:embed / r:id)から画像パーツを引くための対応表 / A lookup from relationship ID to image part.
        /// </summary>
        private readonly Dictionary<string, ImagePart> _imagePartsByRelationshipId = new Dictionary<string, ImagePart>();

        /// <summary>
        /// OCR済み画像のパーツURI。二重出力の抑止と、末尾へ回す取りこぼしの判定に使います。
        /// The URIs of images already processed, used to avoid duplicates and to detect leftovers.
        /// </summary>
        private readonly HashSet<string> _processedImageUris = new HashSet<string>();

        public WordImageContext(MainDocumentPart mainPart)
        {
            MainPart = mainPart;

            foreach (var pair in mainPart.Parts)
            {
                if (pair.OpenXmlPart is ImagePart imagePart)
                {
                    _imagePartsByRelationshipId[pair.RelationshipId] = imagePart;
                }
            }
        }

        /// <summary>
        /// 参照IDに対応する画像パーツを取得します。
        /// Gets the image part corresponding to a relationship ID.
        /// </summary>
        public bool TryGetImagePart(string relationshipId, out ImagePart imagePart)
            => _imagePartsByRelationshipId.TryGetValue(relationshipId, out imagePart!);

        /// <summary>
        /// 画像を処理済みとして記録します。まだ処理されていなかった場合のみ true を返します。
        /// Marks an image as processed. Returns true only if it had not been processed yet.
        /// </summary>
        public bool MarkProcessed(ImagePart imagePart) => _processedImageUris.Add(imagePart.Uri.ToString());
    }

    /// <summary>
    /// Wordの表(Table)をMarkdownのテーブル記法へ変換して追記します。
    /// Converts a Word table into Markdown table syntax and appends it.
    /// </summary>
    /// <param name="table">変換対象の表 / The table to convert.</param>
    /// <param name="sb">出力先のStringBuilder / The destination StringBuilder.</param>
    /// <param name="nestedTableNumbering">入れ子の表へ通し番号を振るカウンタ / The counter numbering nested tables.</param>
    /// <param name="pendingNestedTables">出力待ちの入れ子の表。最上位の呼び出しでは null / The nested tables awaiting output, or null at the top-level call.</param>
    private void AppendWordTable(
        DocumentFormat.OpenXml.Wordprocessing.Table table,
        StringBuilder sb,
        WordNestedTableNumbering nestedTableNumbering,
        List<(int Number, int RowNumber, int ColumnNumber, DocumentFormat.OpenXml.Wordprocessing.Table Table)>? pendingNestedTables = null)
    {
        // 最上位の表の呼び出しが待ち行列を所有し、最後にまとめて出力する。
        // 入れ子の表からの再帰では呼び出し元の行列へ積むため、番号どおりの順序で出力される。
        // The top-level call owns the queue and flushes it at the end. Recursive calls from a nested
        // table push onto the caller's queue, so everything is emitted in numbering order.
        bool ownsPendingNestedTables = pendingNestedTables == null;
        pendingNestedTables ??= new List<(int, int, int, DocumentFormat.OpenXml.Wordprocessing.Table)>();

        var rows = new List<List<string>>();

        foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
        {
            var cells = new List<string>();

            // 参照の見出しに使う行番号。空の行は出力されないため、出力される表の行番号と一致する。
            // The row number used in the reference caption. Empty rows are not emitted, so this
            // matches the row number of the rendered table.
            int rowNumber = rows.Count + 1;

            foreach (var cell in row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
            {
                int columnNumber = cells.Count + 1;

                // Markdownのテーブルは入れ子にできないため、セルには参照だけを残し、
                // 表そのものは外側の表の直後へ独立したテーブルとして出力する。
                // Markdown tables cannot nest, so the cell keeps only a reference and the table
                // itself is emitted as a standalone table right after the outer one.
                var references = new List<string>();
                foreach (var nestedTable in GetNestedTablesInCell(cell))
                {
                    int number = nestedTableNumbering.Next();
                    references.Add(FormatNestedTableReference(number));
                    pendingNestedTables.Add((number, rowNumber, columnNumber, nestedTable));
                }

                cells.Add(GetWordCellText(cell, references));

                // 横結合(gridSpan)されたセルは1つのセルとして現れるため、結合された列数ぶんの
                // 空セルを補い、他の行と列数が揃うようにする。
                // A horizontally merged cell (gridSpan) appears as a single cell, so pad it with empty
                // cells to keep the column count aligned with the other rows.
                int gridSpan = cell.TableCellProperties?.GridSpan?.Val?.Value ?? 1;
                for (int i = 1; i < gridSpan; i++)
                {
                    cells.Add(string.Empty);
                }
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        if (rows.Count == 0) return;

        // 行ごとに列数が異なるとMarkdownの表として崩れるため、最大列数に合わせて空セルで埋める
        // Rows with differing column counts break the Markdown table, so pad them to the maximum count.
        int columnCount = rows.Max(r => r.Count);
        foreach (var row in rows)
        {
            while (row.Count < columnCount) row.Add(string.Empty);
        }

        // 表の前後に空行が無いとMarkdownのテーブルとして認識されない
        // Without blank lines around it, the table is not recognized as a Markdown table.
        sb.AppendLine();
        sb.AppendLine($"| {string.Join(" | ", rows[0])} |");
        sb.AppendLine($"| {string.Join(" | ", Enumerable.Repeat("---", columnCount))} |");

        foreach (var row in rows.Skip(1))
        {
            sb.AppendLine($"| {string.Join(" | ", row)} |");
        }
        sb.AppendLine();

        // 入れ子の表は、所有者である最上位の表の直後へまとめて出力する
        // Nested tables are flushed right after the top-level table that owns the queue.
        if (!ownsPendingNestedTables) return;

        // 出力中にさらに深い入れ子が積まれて行列が伸びるため、添字で走査する
        // The queue grows while it is being flushed (tables nested deeper), so it is walked by index.
        for (int i = 0; i < pendingNestedTables.Count; i++)
        {
            var pending = pendingNestedTables[i];

            sb.AppendLine($"**{FormatNestedTableReference(pending.Number)}** (row {pending.RowNumber}, column {pending.ColumnNumber})");
            AppendWordTable(pending.Table, sb, nestedTableNumbering, pendingNestedTables);
        }
    }

    /// <summary>
    /// 入れ子の表を指す参照の表記を返します。セル内の参照と、表本体の見出しの両方で使います。
    /// Returns the reference label for a nested table, used both inside the cell and as the caption
    /// of the table itself.
    /// </summary>
    /// <param name="number">入れ子の表の通し番号 / The sequential number of the nested table.</param>
    /// <returns>参照の表記 / The reference label.</returns>
    private static string FormatNestedTableReference(int number) => $"[Nested Table {number}]";

    /// <summary>
    /// セルの中に直接置かれた入れ子の表を、出現順に列挙します。
    /// さらに深い入れ子は、その親となる表を出力するときに拾われるため、ここでは返しません。
    /// テキストボックスの中に置かれた表も対象に含めます（セルのテキストからは除外されるため）。
    /// Enumerates the tables nested directly inside a cell, in document order.
    /// Tables nested deeper are picked up when their parent table is emitted, so they are not
    /// returned here. Tables placed inside a text box are included, since they are excluded from
    /// the cell's own text.
    /// </summary>
    /// <param name="cell">対象のセル / The target cell.</param>
    /// <returns>入れ子の表の列挙 / The enumeration of nested tables.</returns>
    private static IEnumerable<DocumentFormat.OpenXml.Wordprocessing.Table> GetNestedTablesInCell(DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
        => cell.Descendants<DocumentFormat.OpenXml.Wordprocessing.Table>()
            .Where(t => !IsInDiscardedAlternateContentBranch(t))
            .Where(t => !IsInsideNestedTable(t, cell));

    /// <summary>
    /// 要素がセル内の入れ子の表の配下にあるかどうかを判定します。
    /// セル自身に属する内容と、入れ子の表に属する内容を切り分けるために使います。
    /// テキストボックスは表ではないため、その中身はセル自身の内容として扱われます。
    /// Determines whether an element sits under a table nested inside the cell.
    /// It separates the cell's own content from the content belonging to a nested table.
    /// A text box is not a table, so its content counts as the cell's own.
    /// </summary>
    /// <param name="element">判定対象の要素 / The element to inspect.</param>
    /// <param name="cell">基準となるセル / The cell used as the boundary.</param>
    /// <returns>入れ子の表の配下にある場合は true / true when the element is under a nested table.</returns>
    private static bool IsInsideNestedTable(OpenXmlElement element, DocumentFormat.OpenXml.Wordprocessing.TableCell cell)
    {
        for (var ancestor = element.Parent; ancestor != null && !ReferenceEquals(ancestor, cell); ancestor = ancestor.Parent)
        {
            if (ancestor is DocumentFormat.OpenXml.Wordprocessing.Table) return true;
        }

        return false;
    }

    /// <summary>
    /// 文書内の入れ子の表へ通し番号を振るためのカウンタです。
    /// ファイル単位で並列処理されるため、インスタンスの状態にはせず文書ごとに生成します。
    /// A counter that numbers the nested tables within a document.
    /// Files are processed in parallel, so it is created per document rather than kept as instance state.
    /// </summary>
    private sealed class WordNestedTableNumbering
    {
        private int _count;

        /// <summary>
        /// 次の通し番号を採番します / Allocates the next sequential number.
        /// </summary>
        public int Next() => ++_count;
    }

    /// <summary>
    /// 表のセル内のテキストを、Markdownのテーブル1セルに収まる形へ整形して返します。
    /// Returns the text of a table cell, formatted to fit in a single Markdown table cell.
    /// </summary>
    /// <param name="cell">対象のセル / The target cell.</param>
    /// <param name="nestedTableReferences">セルへ残す入れ子の表への参照 / The nested table references to keep in the cell.</param>
    /// <returns>整形済みのセルテキスト / The formatted cell text.</returns>
    private string GetWordCellText(
        DocumentFormat.OpenXml.Wordprocessing.TableCell cell,
        IReadOnlyList<string> nestedTableReferences)
    {
        // Markdownの表は1セルを1行で表すため、セル内の複数段落は改行タグで連結する。
        // ただしHTMLタグ無害化が有効な場合は<br>も置換されてしまうため、空白で連結する。
        // A Markdown table cell must stay on one line, so multiple paragraphs are joined with <br>.
        // When HTML sanitization is enabled, <br> would also be replaced, so a space is used instead.
        string separator = _settings.SanitizeHtmlTags ? " " : "<br>";

        // セル内のテキストボックスの中身も段落として列挙されるため、GetWordParagraphText で
        // 段落自身のテキストだけを取り出す（そうしないと入れ子の段落ぶん内容が重複する）。
        // 採用しない mc:AlternateContent のブランチは、同じ内容の二重出力になるため除外する。
        // Text box content inside the cell is enumerated as paragraphs too, so GetWordParagraphText
        // takes only each paragraph's own text (otherwise nested paragraphs are counted twice).
        // The discarded mc:AlternateContent branch is skipped, as it repeats the same content.
        // 入れ子の表の中身はこのセルの文字ではないため除外する。参照だけを末尾へ添えて、
        // 表の直後に出力される本体と対応付けられるようにする。
        // The content of a nested table is not this cell's text, so it is excluded. Only the
        // reference is appended, tying the cell to the table emitted right after the outer one.
        var cellParts = cell.Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Where(p => !IsInDiscardedAlternateContentBranch(p))
            .Where(p => !IsInsideNestedTable(p, cell))
            .Select(p => GetWordParagraphText(p).Replace("\r", " ").Replace("\n", " ").Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .Concat(nestedTableReferences);

        // セル内の "|" は列の区切りと解釈されるためエスケープする
        // Escape "|" inside a cell, otherwise it is interpreted as a column separator.
        return string.Join(separator, cellParts).Replace("|", "\\|");
    }

    /// <summary>
    /// PowerPointファイル(.pptx)を読み込み、スライド単位のセクションに分けて返します。
    /// Reads a PowerPoint file (.pptx) and returns it split into one section per slide.
    /// </summary>
    /// <param name="filePath">読み込むPowerPointファイルのパス / The path of the PowerPoint file to read.</param>
    /// <returns>スライドごとのセクション一覧 / The list of sections, one per slide.</returns>
    private List<string> ReadPowerPointFile(string filePath)
    {
        var sections = new List<string>();
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
                                    // スライドごとに独立したセクションとして組み立てる（容量分割の単位になる）
                                    // Build each slide as its own section, the unit used for size splitting.
                                    var sb = new StringBuilder();
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

                                    sections.Add(sb.ToString());
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
            return new List<string>();
        }

        return sections;
    }

    /// <summary>
    /// プロジェクト内の各ファイルに対して、個別のMarkdownコンテンツを生成します。
    /// しきい値を超えたファイルは複数のパートへ分割され、ファイル名に連番が付きます。
    /// Generates individual Markdown content for each file in the project.
    /// Files exceeding the threshold are split into several parts, numbered in the file name.
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
            var parts = markdownByIndex[i];
            if (parts.Count == 0) continue;

            // 元の相対パスを取得し、末尾に .md を追加する（例: "src/Utils.cs" -> "src/Utils.cs.md"）
            // Get the original relative path and append .md to the end (e.g., "src/Utils.cs" -> "src/Utils.cs.md").
            string relativePath = Path.GetRelativePath(_settings.ProjectPath, allFiles[i]);

            for (int partIndex = 0; partIndex < parts.Count; partIndex++)
            {
                // 分割された場合のみ連番を挟む（例: "src/Utils.cs.1.md"）。
                // 分割されていないファイルの名前は従来どおり変わらない。
                // Insert a sequence number only when split (e.g. "src/Utils.cs.1.md").
                // The name of an unsplit file stays exactly as before.
                string markdownRelativePath = parts.Count == 1
                    ? relativePath + ".md"
                    : $"{relativePath}.{partIndex + 1}.md";

                fileContents.Add((markdownRelativePath, parts[partIndex]));
            }
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
    private List<string>[] GenerateMarkdownInParallel(List<string> allFiles)
    {
        var results = new List<string>[allFiles.Count];

        // 並列度は論理プロセッサ数に制限する。無制限だと画像ごとにTesseractEngineを大量生成し、
        // メモリやネイティブライブラリへの負荷が過大になるため。
        // Cap parallelism at the logical processor count. Unbounded parallelism would spawn many
        // TesseractEngine instances per image, overloading memory and the native library.
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount)
        };

        // PDFや画像などのバイナリはテキストとして読むと文字化けするため、内容を抽出せず
        // 存在だけを示すプレースホルダに置き換える。判定結果は配列へ入れ、あとで元の順序どおりに記録する。
        // Binaries such as PDFs and images turn into mojibake when read as text, so their content is
        // replaced with a placeholder that only records their presence. The decisions go into an array
        // and are collected afterwards in the original file order.
        var isBinary = new bool[allFiles.Count];

        // GenerateMarkdownPartsForFile は共有可変状態を持たない（_settings は読み取り専用、一時ファイルは一意名、
        // 例外は内部で捕捉）ため、ファイル単位の並列実行はスレッドセーフである。
        // GenerateMarkdownPartsForFile has no shared mutable state (read-only _settings, unique temp file
        // names, exceptions caught internally), so per-file parallel execution is thread-safe.
        Parallel.For(0, allFiles.Count, parallelOptions, i =>
        {
            // Office形式は中身がZIPやOLEでバイナリと判定されてしまうため、専用の抽出処理を先に通す
            // Office formats are ZIP or OLE containers and would be flagged as binary, so the files
            // with a dedicated extractor bypass the check.
            if (!HasDedicatedExtractor(allFiles[i]) && BinaryFileDetector.IsBinaryFile(allFiles[i]))
            {
                isBinary[i] = true;
                results[i] = new List<string> { RenderSkippedBinaryFile(allFiles[i]) };
                return;
            }

            results[i] = GenerateMarkdownPartsForFile(allFiles[i]);
        });

        _skippedBinaryFiles.Clear();
        for (int i = 0; i < allFiles.Count; i++)
        {
            if (isBinary[i]) _skippedBinaryFiles.Add(allFiles[i]);
        }

        return results;
    }

    /// <summary>
    /// 内容を抽出しなかったバイナリファイルを、存在を示すプレースホルダとして描画します。
    /// ツリーには載るファイルの内容がなぜ無いのかをAIへ伝え、内容を推測させないための出力です。
    /// Renders a binary file whose content was not extracted as a placeholder recording its presence.
    /// It tells the AI why a file listed in the tree has no content, so that it does not invent one.
    /// </summary>
    /// <param name="filePath">対象ファイルのパス / The path of the target file.</param>
    /// <returns>描画されたMarkdown / The rendered Markdown.</returns>
    private string RenderSkippedBinaryFile(string filePath)
    {
        string relativePath = Path.GetRelativePath(_settings.ProjectPath, filePath);

        var note = new StringBuilder();
        note.Append($"[Skipped: unsupported binary file ({Path.GetExtension(filePath)}, {FormatFileSize(filePath)}). Its content was not extracted.");

        // ファイル出力時のみ原本をコピーするため、コピー先の案内も出力時だけ添える
        // The original is copied only when writing to files, so mention the copy only in that case.
        if (_settings.OutputToFile)
        {
            string copiedPath = $"{AnalyzerSettings.SkippedFilesDirectoryName}/{relativePath.Replace('\\', '/')}";
            note.Append($" The original file is copied to `{copiedPath}`.");
        }

        note.Append(']');

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"## {Path.GetFileName(filePath)}");
        sb.AppendLine();
        sb.AppendLine($"**Relative Path:** `{relativePath}`");
        sb.AppendLine();
        sb.AppendLine($"**File Content:** {note}");
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// ファイルサイズを人が読みやすい単位の文字列にします。取得に失敗した場合は "unknown size" を返します。
    /// Formats a file's size into a human-readable string, or "unknown size" when it cannot be read.
    /// </summary>
    /// <param name="filePath">対象ファイルのパス / The path of the target file.</param>
    /// <returns>整形されたサイズ / The formatted size.</returns>
    private static string FormatFileSize(string filePath)
    {
        try
        {
            double size = new FileInfo(filePath).Length;
            string[] units = { "B", "KB", "MB", "GB" };

            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:0} {units[unitIndex]}"
                : $"{size:0.#} {units[unitIndex]}";
        }
        catch (IOException)
        {
            return "unknown size";
        }
    }
}