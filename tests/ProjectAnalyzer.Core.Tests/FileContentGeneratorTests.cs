using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

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
}