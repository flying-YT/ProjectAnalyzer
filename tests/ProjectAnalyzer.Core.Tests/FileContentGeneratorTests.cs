using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace ProjectAnalyzer.Core.Tests;

public class FileContentGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public FileContentGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "TestCode.cs"), "public class A {}");
        File.WriteAllText(Path.Combine(_tempDir, "IgnoreMe.txt"), "secret");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Generate_CreatesMarkdownContextWithHighlighting()
    {
        // Arrange
        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string> { "IgnoreMe.txt" }, outputToFile: false, omitCodeBlockTicks: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert
        Assert.Single(results); // サイズが小さいので1つの要素にまとまる
        var content = results[0];

        // 内容の検証
        Assert.Contains("## TestCode.cs", content);
        Assert.Contains("```csharp", content);
        Assert.Contains("public class A {}", content);

        // 除外ファイルの検証
        Assert.DoesNotContain("IgnoreMe.txt", content);
        Assert.DoesNotContain("secret", content);
    }

    [Fact]
    public void Generate_SanitizesHtmlTags_WhenSanitizeHtmlTagsIsTrue()
    {
        // Arrange
        var tempFile = Path.Combine(_tempDir, "HtmlFile.html");
        File.WriteAllText(tempFile, "<html>\n<body>\n<div class=\"test\">if (a < b)</div>\n</body>\n</html>");
        
        var settings = new AnalyzerSettings(
            _tempDir, 
            "", 
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" }, 
            outputToFile: false, 
            omitCodeBlockTicks: false, 
            outputPerFile: false, 
            tempClonePath: null, 
            sanitizeHtmlTags: true // ここをTrueにする
        );
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert
        var content = results[0];
        
        // 自前で出力しているdetails/summaryが置換されているか
        Assert.Contains("＜details＞", content);
        Assert.Contains("＜summary＞View content＜/summary＞", content);
        Assert.Contains("＜/details＞", content);
        
        // コンテンツ内のHTMLタグが置換されているか
        Assert.Contains("＜html＞", content);
        Assert.Contains("＜body＞", content);
        Assert.Contains("＜div class=\"test\"＞", content);
        Assert.Contains("＜/div＞", content);
        
        // C#などの "a < b" は置換されていないか
        Assert.Contains("if (a < b)", content);
        
        File.Delete(tempFile);
    }

    [Fact]
    public void Generate_PreservesFileOrder_WhenProcessedInParallel()
    {
        // Arrange: 多数のファイルを作成し、並列処理でも出力順序が維持されることを検証する
        // Create many files to verify the output order is preserved even under parallel processing.
        const int fileCount = 50;
        for (int i = 0; i < fileCount; i++)
        {
            // ファイル名がソート順で安定するようゼロ埋めする（例: File_00.cs, File_01.cs, ...）
            string name = $"File_{i:D2}.cs";
            File.WriteAllText(Path.Combine(_tempDir, name), $"// content {i:D2}");
        }

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();
        var content = string.Concat(results);

        // Assert: 各ファイルの見出しが、ファイル名のソート順どおりに並んでいること
        int previousIndex = -1;
        for (int i = 0; i < fileCount; i++)
        {
            int headingIndex = content.IndexOf($"## File_{i:D2}.cs", StringComparison.Ordinal);
            Assert.True(headingIndex >= 0, $"File_{i:D2}.cs が出力に含まれていません。");
            Assert.True(headingIndex > previousIndex, $"File_{i:D2}.cs の順序が崩れています。");
            previousIndex = headingIndex;
        }
    }

    [Fact]
    public void Generate_RemovesIndent_WhenRemoveIndentIsTrue()
    {
        // Arrange
        var tempFile = Path.Combine(_tempDir, "IndentTest.cs");
        File.WriteAllText(tempFile, "class A\n{\n    void M()\n    {\n        int x = 1;\n    }\n}");
        
        var settings = new AnalyzerSettings(
            _tempDir, 
            "", 
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" }, 
            outputToFile: false, 
            omitCodeBlockTicks: false, 
            outputPerFile: false, 
            tempClonePath: null, 
            sanitizeHtmlTags: false,
            removeIndent: true // Trueにする
        );
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert
        var content = results[0];
        
        // インデントがすべて削除されていることを検証
        Assert.Contains("class A\n{\nvoid M()\n{\nint x = 1;\n}\n}", content.Replace("\r\n", "\n"));

        File.Delete(tempFile);
    }

    [Fact]
    public void Generate_ConvertsWordTableToMarkdownTable()
    {
        // Arrange: 段落 → 表 → 段落 の順に並んだ .docx を作成する
        // Create a .docx laid out as paragraph -> table -> paragraph.
        var tempFile = Path.Combine(_tempDir, "TableDoc.docx");
        CreateWordFileWithTable(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: 1行目がヘッダー、2行目が区切り行のMarkdownテーブルになっていること
        Assert.Contains("| 項目 | 説明 |\n| --- | --- |\n", content);

        // セル内の "|" がエスケープされ、列区切りとして解釈されないこと
        Assert.Contains("| A | あ\\|あ |", content);

        // セル内の複数段落が <br> で1行にまとめられていること
        Assert.Contains("| B | 1行目<br>2行目 |", content);

        // 横結合(gridSpan)された行も、空セルで列数が揃えられていること
        Assert.Contains("| 結合セル |  |", content);

        // 段落と表の出現順序が元の文書どおりであること
        int beforeIndex = content.IndexOf("導入の段落", StringComparison.Ordinal);
        int tableIndex = content.IndexOf("| 項目 | 説明 |", StringComparison.Ordinal);
        int afterIndex = content.IndexOf("末尾の段落", StringComparison.Ordinal);
        Assert.True(beforeIndex >= 0 && beforeIndex < tableIndex, "表の前の段落が正しい位置にありません。");
        Assert.True(tableIndex < afterIndex, "表の後の段落が正しい位置にありません。");
    }

    [Fact]
    public void Generate_SkipsEmbeddedImages_WhenOcrIsDisabled()
    {
        // Arrange: 画像を埋め込んだ .docx を作成する（OCRは無効のまま）
        var tempFile = Path.Combine(_tempDir, "ImageDoc.docx");
        CreateWordFileWithImage(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate());

        // Assert: 本文は抽出されるが、OCR無効時は画像セクションが出力されないこと
        Assert.Contains("画像付きの段落", content);
        Assert.DoesNotContain("[Embedded Images]", content);
    }

    [Fact]
    public void Generate_PlacesEmbeddedImageOcr_AtTheParagraphWhereTheImageAppears()
    {
        // Arrange: 段落 → 画像の段落 → 段落 の順に並んだ .docx を作成し、OCRを有効にする
        // Create a .docx laid out as paragraph -> image paragraph -> paragraph, with OCR enabled.
        var tempFile = Path.Combine(_tempDir, "InlineImageDoc.docx");
        CreateWordFileWithInlineImage(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true,
            enableOcr: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: OCR結果が、画像を含む段落の位置（前後の段落の間）へ出力されていること。
        // OCRの成否は環境に依存するため、必ず出力される見出し行の位置で検証する。
        // The OCR result must be emitted between the surrounding paragraphs. OCR success depends on the
        // environment, so the position is verified with the header line that is always emitted.
        int beforeIndex = content.IndexOf("画像の前の段落", StringComparison.Ordinal);
        int ocrIndex = content.IndexOf("--- Embedded Image 1", StringComparison.Ordinal);
        int afterIndex = content.IndexOf("画像の後の段落", StringComparison.Ordinal);

        Assert.True(beforeIndex >= 0, "画像の前の段落が抽出されていません。");
        Assert.True(ocrIndex >= 0, "埋め込み画像のOCR結果が出力されていません。");
        Assert.True(afterIndex >= 0, "画像の後の段落が抽出されていません。");
        Assert.True(beforeIndex < ocrIndex && ocrIndex < afterIndex, "OCR結果が画像の位置に出力されていません。");

        // 位置が特定できた画像は、末尾のフォールバックセクションへは出力されないこと
        // An image whose position was determined must not also land in the trailing fallback section.
        Assert.DoesNotContain("[Embedded Images]", content);
    }

    [Fact]
    public void Generate_PlacesUnreferencedImageOcr_AtTheEndOfTheDocument()
    {
        // Arrange: 本文から参照されていない画像を含む .docx を作成し、OCRを有効にする
        // Create a .docx containing an image that is not referenced from the body, with OCR enabled.
        var tempFile = Path.Combine(_tempDir, "OrphanImageDoc.docx");
        CreateWordFileWithImage(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true,
            enableOcr: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: 差し込み位置を特定できない画像は、取りこぼさず末尾のセクションへ出力されること
        // An image whose position cannot be determined must still be emitted in the trailing section.
        int bodyIndex = content.IndexOf("画像付きの段落", StringComparison.Ordinal);
        int sectionIndex = content.IndexOf("### [Embedded Images]", StringComparison.Ordinal);

        Assert.True(bodyIndex >= 0, "本文が抽出されていません。");
        Assert.True(sectionIndex > bodyIndex, "参照されていない画像のセクションが本文の後に出力されていません。");
        Assert.Contains("--- Embedded Image 1", content);
    }

    [Fact]
    public void Generate_SplitsWordIntoParts_PerTopLevelHeading_WhenExceedingMaxOutputSize()
    {
        // Arrange: 見出しスタイルを使った .docx を作成する。
        // 日本語版Wordを模し、スタイルIDは "Heading1" ではなく自動生成風の "a3" にしている。
        // Create a .docx that uses heading styles. Mimicking Japanese Word, the style ID is an
        // auto-generated looking "a3" rather than "Heading1".
        var tempFile = Path.Combine(_tempDir, "HeadingDoc.docx");
        CreateWordFileWithHeadings(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true,
            maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: 前書き・第1章・第2章の3セクションに分かれること
        // The preamble and the two chapters become three separate sections.
        Assert.Equal(3, results.Count);
        Assert.Contains("前書きの段落", results[0]);
        Assert.Contains("### 第1章", results[1]);
        Assert.DoesNotContain("### 第2章", results[1]);
        Assert.Contains("### 第2章", results[2]);

        // H2はMarkdownの見出しになるが、分割の境界にはならないこと
        // A level-2 heading becomes a Markdown heading but is not a split boundary.
        Assert.Contains("#### 第1章の節", results[1]);

        // どのパートにもファイル名と相対パスが再掲され、details が閉じていること
        foreach (var part in results)
        {
            Assert.Contains("## HeadingDoc.docx (", part);
            Assert.Contains("**Relative Path:** `HeadingDoc.docx`", part);
            Assert.Equal(1, CountOccurrences(part, "<details>"));
            Assert.Equal(1, CountOccurrences(part, "</details>"));
        }
    }

    [Fact]
    public void Generate_DoesNotSplitWord_WhenNoHeadingStylesAreUsed()
    {
        // Arrange: 見出しスタイルを使わない大きな .docx を作成する
        // Create a large .docx that does not use heading styles.
        var tempFile = Path.Combine(_tempDir, "FlatDoc.docx");
        CreateWordFileWithPlainParagraphs(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true,
            maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: 分割の境界が無いため、しきい値を超えたまま1つにまとまること
        // With no split boundary it stays as one oversized output.
        Assert.Single(results);
        Assert.DoesNotContain("**Part:**", results[0]);
    }

    [Fact]
    public void Generate_DoesNotSplitPlainTextFiles_EvenWhenExceedingMaxOutputSize()
    {
        // Arrange: しきい値を大きく超えるテキストファイルを作成する。
        // 内容に "### " で始まる行を含め、ソースコードのコメントが見出しと誤検出されないことも確かめる。
        // Create a text file far above the threshold, including a line starting with "### " to confirm
        // that comments in source code are not mistaken for headings.
        var tempFile = Path.Combine(_tempDir, "Large.txt");
        File.WriteAllText(tempFile, "### これは見出しではありません\n" + new string('a', 5000));

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true,
            maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: プレーンテキストは分割対象外なので、1つのまま出力されること
        Assert.Single(results);
        Assert.DoesNotContain("**Part:**", results[0]);
        Assert.Contains("### これは見出しではありません", results[0]);
    }

    [Fact]
    public void Generate_OmitsCodeBlock_WhenOmitCodeBlockTicksIsTrue()
    {
        // Arrange
        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate());

        // Assert: details ブロックの本文は残るが、コードブロックは出力されないこと
        Assert.Contains("<details>", content);
        Assert.Contains("public class A {}", content);
        Assert.DoesNotContain("```", content);
    }

    [Fact]
    public void Generate_SplitsIntoMultipleContexts_WhenExceedingMaxFileSize()
    {
        // Arrange: 1ファイルあたり約1.2MB（Markdown上は本文が2回出力されるため約2.4MB）のファイルを2つ作成し、
        // 合計が上限の4MBを超えるようにする。
        const int contentLength = 1_200_000;
        for (int i = 0; i < 2; i++)
        {
            File.WriteAllText(Path.Combine(_tempDir, $"Large_{i}.txt"), new string('a', contentLength));
        }

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: 上限を超えた分が別のコンテキストへ分割されること
        Assert.Equal(2, results.Count);
        Assert.StartsWith("# \U0001f4c4 Project Context", results[0]);
        Assert.StartsWith("# \U0001f4c4 Project Context (続き)", results[1]);

        // 分割後もすべてのファイルが失われていないこと
        var joined = string.Concat(results);
        Assert.Contains("## Large_0.txt", joined);
        Assert.Contains("## Large_1.txt", joined);
    }

    [Fact]
    public void GeneratePerFile_ReturnsMarkdownPerFile_WithRelativePathsPreserved()
    {
        // Arrange: サブディレクトリを含む構成にする
        var subDir = Path.Combine(_tempDir, "SubDir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Nested.cs"), "public class Nested {}");

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: false,
            outputPerFile: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.GeneratePerFile();

        // Assert: 除外ファイルを除いた各ファイルが1件ずつ返ること
        Assert.Equal(2, results.Count);

        // 元の相対パスに .md を付けたパスになっていること
        var root = Assert.Single(results, r => r.RelativePath == "TestCode.cs.md");
        Assert.Contains("public class A {}", root.Content);

        string nestedPath = Path.Combine("SubDir", "Nested.cs") + ".md";
        var nested = Assert.Single(results, r => r.RelativePath == nestedPath);
        Assert.Contains("public class Nested {}", nested.Content);

        // 除外ファイルが含まれないこと
        Assert.DoesNotContain(results, r => r.RelativePath.Contains("IgnoreMe"));
    }

    /// <summary>
    /// テスト用に、段落と表を含む .docx ファイルを生成します。
    /// </summary>
    private static void CreateWordFileWithTable(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        body.AppendChild(CreateParagraph("導入の段落"));

        var table = new Table();
        table.AppendChild(CreateRow("項目", "説明"));
        table.AppendChild(CreateRow("A", "あ|あ"));

        // 2つの段落を持つセル / A cell containing two paragraphs.
        table.AppendChild(new TableRow(
            new TableCell(CreateParagraph("B")),
            new TableCell(CreateParagraph("1行目"), CreateParagraph("2行目"))));

        // 2列ぶん横結合されたセルのみの行 / A row with a single cell spanning two columns.
        table.AppendChild(new TableRow(
            new TableCell(
                new TableCellProperties(new GridSpan { Val = 2 }),
                CreateParagraph("結合セル"))));

        body.AppendChild(table);
        body.AppendChild(CreateParagraph("末尾の段落"));

        mainPart.Document.Save();
    }

    /// <summary>
    /// テスト用に、埋め込み画像を含む .docx ファイルを生成します。
    /// </summary>
    private static void CreateWordFileWithImage(string path)
    {
        // 1x1ピクセルのPNG（OCRは行わないため内容は問わない）
        byte[] pngBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(CreateParagraph("画像付きの段落")));

        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var imageStream = new MemoryStream(pngBytes))
        {
            imagePart.FeedData(imageStream);
        }

        mainPart.Document.Save();
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>
    /// テスト用に、見出しスタイルを使った .docx ファイルを生成します。
    /// スタイルIDは日本語版Wordを模して "Heading1" ではない自動生成風の値にしています。
    /// </summary>
    private static void CreateWordFileWithHeadings(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        // スタイル定義側に正規名 "heading 1" / "heading 2" を持たせる
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new Style(new StyleName { Val = "heading 1" }) { Type = StyleValues.Paragraph, StyleId = "a3" },
            new Style(new StyleName { Val = "heading 2" }) { Type = StyleValues.Paragraph, StyleId = "a4" });
        stylesPart.Styles.Save();

        body.AppendChild(CreateParagraph("前書きの段落"));

        body.AppendChild(CreateStyledParagraph("第1章", "a3"));
        body.AppendChild(CreateStyledParagraph("第1章の節", "a4"));
        body.AppendChild(CreateParagraph(new string('a', 2000)));

        body.AppendChild(CreateStyledParagraph("第2章", "a3"));
        body.AppendChild(CreateParagraph(new string('b', 2000)));

        mainPart.Document.Save();
    }

    /// <summary>
    /// テスト用に、見出しスタイルを使わない大きな .docx ファイルを生成します。
    /// </summary>
    private static void CreateWordFileWithPlainParagraphs(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body(
            CreateParagraph(new string('a', 2500)),
            CreateParagraph(new string('b', 2500))));

        mainPart.Document.Save();
    }

    private static Paragraph CreateStyledParagraph(string text, string styleId) => new Paragraph(
        new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
        new Run(new Text(text)));

    /// <summary>
    /// テスト用に、本文の段落から参照された埋め込み画像を含む .docx ファイルを生成します。
    /// </summary>
    private static void CreateWordFileWithInlineImage(string path)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        var body = mainPart.Document.Body!;

        var imagePart = mainPart.AddImagePart(ImagePartType.Png);
        using (var imageStream = new MemoryStream(OnePixelPng))
        {
            imagePart.FeedData(imageStream);
        }

        body.AppendChild(CreateParagraph("画像の前の段落"));
        body.AppendChild(CreateImageParagraph(mainPart.GetIdOfPart(imagePart)));
        body.AppendChild(CreateParagraph("画像の後の段落"));

        mainPart.Document.Save();
    }

    /// <summary>
    /// 指定した関係IDの画像を参照する段落を生成します（Word本文に画像を配置する標準的な構造）。
    /// </summary>
    private static Paragraph CreateImageParagraph(string relationshipId)
    {
        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = 990000L, Cy = 990000L },
                new DW.DocProperties { Id = 1U, Name = "Picture 1" },
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "image.png" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = 990000L, Cy = 990000L }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture"
                    })));

        return new Paragraph(new Run(drawing));
    }

    /// <summary>
    /// 1x1ピクセルのPNG。OCRの結果は問わず、画像パーツの有無のみを検証するために使用します。
    /// </summary>
    private static byte[] OnePixelPng => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private static Paragraph CreateParagraph(string text) => new Paragraph(new Run(new Text(text)));

    private static TableRow CreateRow(params string[] cellTexts)
    {
        var row = new TableRow();
        foreach (var text in cellTexts)
        {
            row.AppendChild(new TableCell(CreateParagraph(text)));
        }
        return row;
    }

    [Fact]
    public void Generate_ConvertsTableInsideTextBoxToMarkdownTable()
    {
        // Arrange: テキストボックス（図形）の中に表がある .docx を作成する
        // Create a .docx whose table lives inside a text box (a shape).
        var tempFile = Path.Combine(_tempDir, "TextBoxDoc.docx");
        CreateWordFileWithTextBoxTable(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: テキストボックス内の表もMarkdownのテーブルになっていること
        Assert.Contains("| 項目 | 説明 |\n| --- | --- |\n", content);
        Assert.Contains("| A | あ |", content);

        // 1行の文字列へ潰れていないこと（段落の InnerText として連結されていないこと）
        Assert.DoesNotContain("項目説明", content);

        // mc:Choice と mc:Fallback の両方を辿って二重出力していないこと
        Assert.Equal(1, CountOccurrences(content, "| A | あ |"));

        // テキストボックスの前後の段落が失われていないこと
        Assert.Contains("導入の段落", content);
        Assert.Contains("末尾の段落", content);
    }

    [Fact]
    public void Generate_EmitsAlternateContentOnlyOnce()
    {
        // Arrange: 本文直下の mc:AlternateContent に同じ表が2つの形式で入った .docx を作成する
        // Create a .docx whose body holds the same table in both branches of an mc:AlternateContent.
        var tempFile = Path.Combine(_tempDir, "AlternateContentDoc.docx");
        CreateWordFileWithAlternateContentTable(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: 新旧どちらか一方のブランチだけが採用され、表が1回だけ出力されること
        Assert.Contains("| 項目 | 説明 |", content);
        Assert.Equal(1, CountOccurrences(content, "| A | あ |"));

        // 採用しなかったブランチが空のテーブルとして残っていないこと（区切り行の数で判定する）
        Assert.Equal(1, CountOccurrences(content, "| --- | --- |"));
    }

    [Fact]
    public void Generate_ReadsMacroEnabledWordFile()
    {
        // Arrange: マクロ有効形式(.docm)の Word ファイルを作成する
        // Create a macro-enabled (.docm) Word file.
        var tempFile = Path.Combine(_tempDir, "MacroDoc.docm");
        CreateWordFileWithTable(tempFile);

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: .docx と同じようにWordとして読み込まれ、表がMarkdownになっていること
        Assert.Contains("## MacroDoc.docm", content);
        Assert.Contains("| 項目 | 説明 |\n| --- | --- |\n", content);

        // ZIPパッケージの生バイト列がそのまま出力されていないこと
        Assert.DoesNotContain("word/document.xml", content);
    }

    /// <summary>
    /// テスト用に、テキストボックス（図形）の中へ表を配置した .docx ファイルを生成します。
    /// Wordが実際に出力する構造にならって、新形式(mc:Choice)と旧形式(mc:Fallback)の両方へ同じ表を入れています。
    /// </summary>
    private static void CreateWordFileWithTextBoxTable(string path)
    {
        string textBoxXml = $@"
<w:p><w:r>
  <mc:AlternateContent {McNamespace}>
    <mc:Choice Requires=""wps"">
      <w:drawing {WpNamespace}><wp:inline><a:graphic {ANamespace}><a:graphicData>
        <wps:wsp {WpsNamespace}><wps:txbx><w:txbxContent>{SampleTableXml}</w:txbxContent></wps:txbx></wps:wsp>
      </a:graphicData></a:graphic></wp:inline></w:drawing>
    </mc:Choice>
    <mc:Fallback>
      <w:pict {VNamespace}><v:shape><v:textbox><w:txbxContent>{SampleTableXml}</w:txbxContent></v:textbox></v:shape></w:pict>
    </mc:Fallback>
  </mc:AlternateContent>
</w:r></w:p>";

        CreateWordFileFromBodyXml(path, ParagraphXml("導入の段落") + textBoxXml + ParagraphXml("末尾の段落"));
    }

    /// <summary>
    /// テスト用に、本文直下の mc:AlternateContent へ同じ表を2つの形式で入れた .docx ファイルを生成します。
    /// </summary>
    private static void CreateWordFileWithAlternateContentTable(string path)
    {
        string alternateContentXml = $@"
<mc:AlternateContent {McNamespace}>
  <mc:Choice Requires=""wps"">{SampleTableXml}</mc:Choice>
  <mc:Fallback>{SampleTableXml}</mc:Fallback>
</mc:AlternateContent>";

        CreateWordFileFromBodyXml(path, ParagraphXml("導入の段落") + alternateContentXml + ParagraphXml("末尾の段落"));
    }

    /// <summary>
    /// 本文のXMLを直接指定して .docx を生成します。
    /// SDKのクラスでは組み立てにくい mc:AlternateContent やテキストボックスの構造を再現するために使用します。
    /// </summary>
    private static void CreateWordFileFromBodyXml(string path, string bodyXml)
    {
        using var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new Document(new Body());
        mainPart.Document.Body!.InnerXml = bodyXml;
        mainPart.Document.Save();
    }

    private const string McNamespace = @"xmlns:mc=""http://schemas.openxmlformats.org/markup-compatibility/2006""";
    private const string WpNamespace = @"xmlns:wp=""http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing""";
    private const string ANamespace = @"xmlns:a=""http://schemas.openxmlformats.org/drawingml/2006/main""";
    private const string WpsNamespace = @"xmlns:wps=""http://schemas.microsoft.com/office/word/2010/wordprocessingShape""";
    private const string VNamespace = @"xmlns:v=""urn:schemas-microsoft-com:vml""";

    /// <summary>
    /// テストで使い回す2行2列の表のXMLです。
    /// </summary>
    private const string SampleTableXml =
        "<w:tbl>" +
        "<w:tr><w:tc><w:p><w:r><w:t>項目</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>説明</w:t></w:r></w:p></w:tc></w:tr>" +
        "<w:tr><w:tc><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>あ</w:t></w:r></w:p></w:tc></w:tr>" +
        "</w:tbl>";

    private static string ParagraphXml(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    [Fact]
    public void Generate_ExtractsNestedTableAsSeparateTable()
    {
        // Arrange: 外側の表の1セルに入れ子の表が入った .docx を作成する
        // Create a .docx whose outer table holds a nested table in one of its cells.
        var tempFile = Path.Combine(_tempDir, "NestedTableDoc.docx");
        CreateWordFileFromBodyXml(tempFile, TableXml(
            RowXml(CellXml(ParagraphXml("機能ID")), CellXml(ParagraphXml("入力項目"))),
            RowXml(
                CellXml(ParagraphXml("F-001")),
                CellXml(ParagraphXml("※任意項目を含む") + TableXml(
                    RowXml(CellXml(ParagraphXml("項目名")), CellXml(ParagraphXml("型"))),
                    RowXml(CellXml(ParagraphXml("受注番号")), CellXml(ParagraphXml("string"))))))));

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: 外側の表は2列の表のまま保たれ、セルには参照だけが残ること
        Assert.Contains("| 機能ID | 入力項目 |\n| --- | --- |\n", content);
        Assert.Contains("| F-001 | ※任意項目を含む<br>[Nested Table 1] |", content);

        // 入れ子の表が、参照の見出し付きで独立したテーブルとして出力されること
        Assert.Contains("**[Nested Table 1]** (row 2, column 2)", content);
        Assert.Contains("| 項目名 | 型 |\n| --- | --- |\n| 受注番号 | string |", content);

        // セルの中身が読み順に平坦化されていないこと（列と行の対応が保たれていること）
        Assert.DoesNotContain("項目名<br>型", content);

        // 入れ子の表は外側の表より後に出力されること
        int outerIndex = content.IndexOf("| 機能ID | 入力項目 |", StringComparison.Ordinal);
        int nestedIndex = content.IndexOf("**[Nested Table 1]**", StringComparison.Ordinal);
        Assert.True(outerIndex >= 0 && outerIndex < nestedIndex, "入れ子の表が外側の表より前に出力されています。");
    }

    [Fact]
    public void Generate_NumbersNestedTablesSequentially_AcrossNestingLevels()
    {
        // Arrange: 1つの表に入れ子が2つあり、さらにその片方にも入れ子がある .docx を作成する
        // Create a .docx with two nested tables, one of which nests a table of its own.
        var tempFile = Path.Combine(_tempDir, "DeepNestedDoc.docx");
        CreateWordFileFromBodyXml(tempFile, TableXml(RowXml(
            CellXml(ParagraphXml("cell1") + TableXml(RowXml(
                CellXml(ParagraphXml("L2-a")),
                CellXml(ParagraphXml("L2-b") + TableXml(RowXml(CellXml(ParagraphXml("L3-x")), CellXml(ParagraphXml("L3-y")))))))),
            CellXml(ParagraphXml("cell2") + TableXml(RowXml(CellXml(ParagraphXml("other-1")), CellXml(ParagraphXml("other-2"))))))));

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: 参照が採番順どおりに並び、深い入れ子も取りこぼされないこと
        int first = content.IndexOf("**[Nested Table 1]**", StringComparison.Ordinal);
        int second = content.IndexOf("**[Nested Table 2]**", StringComparison.Ordinal);
        int third = content.IndexOf("**[Nested Table 3]**", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < second && second < third, "入れ子の表が採番順に出力されていません。");

        // 3階層目の表が、2階層目のセルから参照されていること
        Assert.Contains("| L2-a | L2-b<br>[Nested Table 3] |", content);
        Assert.Contains("| L3-x | L3-y |", content);

        // 同じ番号が重複して採番されていないこと
        Assert.Equal(1, CountOccurrences(content, "**[Nested Table 1]**"));
        Assert.Equal(1, CountOccurrences(content, "**[Nested Table 2]**"));
        Assert.Equal(1, CountOccurrences(content, "**[Nested Table 3]**"));
    }

    [Fact]
    public void Generate_KeepsTextBoxContentInCell_WhenSeparatingNestedTables()
    {
        // Arrange: セル内のテキストボックスに、文章と表の両方が入った .docx を作成する
        // Create a .docx whose cell holds a text box containing both a paragraph and a table.
        var tempFile = Path.Combine(_tempDir, "TextBoxInCellDoc.docx");

        string textBoxContent = ParagraphXml("枠内テキスト")
            + TableXml(RowXml(CellXml(ParagraphXml("TB-1")), CellXml(ParagraphXml("TB-2"))));

        string textBoxXml = $@"
<w:p><w:r>
  <mc:AlternateContent {McNamespace}>
    <mc:Choice Requires=""wps"">
      <w:drawing {WpNamespace}><wp:inline><a:graphic {ANamespace}><a:graphicData>
        <wps:wsp {WpsNamespace}><wps:txbx><w:txbxContent>{textBoxContent}</w:txbxContent></wps:txbx></wps:wsp>
      </a:graphicData></a:graphic></wp:inline></w:drawing>
    </mc:Choice>
    <mc:Fallback>
      <w:pict {VNamespace}><v:shape><v:textbox><w:txbxContent>{textBoxContent}</w:txbxContent></v:textbox></v:shape></w:pict>
    </mc:Fallback>
  </mc:AlternateContent>
</w:r></w:p>";

        CreateWordFileFromBodyXml(tempFile, TableXml(RowXml(
            CellXml(ParagraphXml("左")),
            CellXml(ParagraphXml("右") + textBoxXml))));

        var settings = new AnalyzerSettings(
            _tempDir,
            "",
            new HashSet<string> { "TestCode.cs", "IgnoreMe.txt" },
            outputToFile: false,
            omitCodeBlockTicks: true);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: テキストボックス内の文章はセルの内容として残ること（入れ子の表と一緒に消さない）
        Assert.Contains("| 左 | 右<br>枠内テキスト<br>[Nested Table 1] |", content);

        // テキストボックス内の表も、独立したテーブルとして1回だけ出力されること
        Assert.Contains("| TB-1 | TB-2 |", content);
        Assert.Equal(1, CountOccurrences(content, "| TB-1 | TB-2 |"));
    }

    private static string TableXml(params string[] rows) => "<w:tbl>" + string.Concat(rows) + "</w:tbl>";

    private static string RowXml(params string[] cells) => "<w:tr>" + string.Concat(cells) + "</w:tr>";

    private static string CellXml(string inner) => $"<w:tc>{inner}</w:tc>";
}
