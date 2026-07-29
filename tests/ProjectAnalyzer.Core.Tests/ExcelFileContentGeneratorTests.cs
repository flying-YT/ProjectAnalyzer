using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using Xdr = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ProjectAnalyzer.Core.Tests;

/// <summary>
/// Excelファイル(.xlsx)の読み込みに関するテストです。
/// Spreadsheet名前空間の型名が他のOffice形式と衝突するため、テストクラスを分けています。
/// </summary>
public class ExcelFileContentGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public ExcelFileContentGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Generate_ReadsExcelFile_WithSheetNameHeadingAndCsvRows()
    {
        // Arrange
        var excelPath = Path.Combine(_tempDir, "Book.xlsx");
        CreateExcelFile(excelPath);

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: シート名が見出しになっていること
        Assert.Contains("### 明細シート", content);

        // 各行がカンマ区切りで出力されていること
        Assert.Contains("項目, 数量", content);
        Assert.Contains("りんご, 3", content);

        // 完全に空の行はスキップされること（空行だけの ", " が出力されない）
        Assert.DoesNotContain("\n, \n", content);
    }

    [Fact]
    public void Generate_PlacesShapesAndImages_InsideEachSheetSection()
    {
        // Arrange: 図形テキストを持つシートを2枚作り、1枚目には画像も埋め込む（OCRは無効のまま）
        // Create two sheets that each hold shape text, with an image embedded in the first one.
        var excelPath = Path.Combine(_tempDir, "Shapes.xlsx");
        CreateExcelFileWithShapes(excelPath);

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: 図形テキストと画像の情報が、それぞれのシートのセクション内に出力されていること
        // The shape text and the image notice must be emitted inside their own sheet's section.
        Assert.Contains("#### [Shapes, TextBoxes & Images]", content);

        int firstSheetIndex = content.IndexOf("### 1枚目", StringComparison.Ordinal);
        int firstShapeIndex = content.IndexOf("1枚目の図形テキスト", StringComparison.Ordinal);
        int imageNoticeIndex = content.IndexOf("画像が見つかりましたが", StringComparison.Ordinal);
        int secondSheetIndex = content.IndexOf("### 2枚目", StringComparison.Ordinal);
        int secondShapeIndex = content.IndexOf("2枚目の図形テキスト", StringComparison.Ordinal);

        Assert.True(firstSheetIndex >= 0 && secondSheetIndex >= 0, "シート見出しが出力されていません。");
        Assert.True(firstShapeIndex >= 0 && secondShapeIndex >= 0, "図形テキストが抽出されていません。");
        Assert.True(imageNoticeIndex >= 0, "画像に関する通知が出力されていません。");

        // 1枚目の図形テキストと画像通知が、2枚目の見出しより前にあること（末尾へ寄せられていないこと）
        // The first sheet's content must appear before the second sheet's heading, not pushed to the end.
        Assert.True(firstSheetIndex < firstShapeIndex, "1枚目の図形テキストがシートのセクション内にありません。");
        Assert.True(firstShapeIndex < imageNoticeIndex, "画像の通知が1枚目のセクション内にありません。");
        Assert.True(imageNoticeIndex < secondSheetIndex, "1枚目の内容が2枚目のセクションより後に出力されています。");
        Assert.True(secondSheetIndex < secondShapeIndex, "2枚目の図形テキストがシートのセクション内にありません。");
    }

    [Fact]
    public void Generate_SplitsExcelIntoParts_PerSheet_WhenExceedingMaxOutputSize()
    {
        // Arrange: それぞれ単体でしきい値を超えるシートを2枚持つ .xlsx を作成する
        // Create a .xlsx with two sheets that each exceed the threshold on their own.
        var excelPath = Path.Combine(_tempDir, "Book.xlsx");
        CreateExcelFileWithSheets(excelPath, ("シートA", new string('a', 2000)), ("シートB", new string('b', 2000)));

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false, maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: シート単位で別のコンテキストへ分割されること
        Assert.Equal(2, results.Count);
        Assert.Contains("### シートA", results[0]);
        Assert.DoesNotContain("### シートB", results[0]);
        Assert.Contains("### シートB", results[1]);

        // どのパートにもファイル名と相対パスが共通ヘッダとして再掲されること
        // Every part repeats the file name and the relative path as a shared header.
        foreach (var part in results)
        {
            Assert.Contains("## Book.xlsx (", part);
            Assert.Contains("**Relative Path:** `Book.xlsx`", part);

            // details とコードブロックがパート内で閉じていること
            // The details block and the code block are closed within the part.
            Assert.Equal(CountOccurrences(part, "<details>"), CountOccurrences(part, "</details>"));
            Assert.Equal(1, CountOccurrences(part, "<details>"));
            Assert.Equal(0, CountOccurrences(part, "```") % 2);
        }

        // パート番号が付与されること
        Assert.Contains("**Part:** 1/2", results[0]);
        Assert.Contains("**Part:** 2/2", results[1]);
    }

    [Fact]
    public void Generate_DoesNotSplitExcel_WhenItHasOnlyOneSheet()
    {
        // Arrange: しきい値を大きく超えるが、シートが1枚しかない .xlsx を作成する
        // Create a .xlsx that far exceeds the threshold but has only one sheet.
        var excelPath = Path.Combine(_tempDir, "Single.xlsx");
        CreateExcelFileWithSheets(excelPath, ("唯一のシート", new string('a', 5000)));

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false, maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: 分割の境界が無いため、しきい値を超えたまま1つにまとまること
        // With no split boundary it stays as one oversized output.
        Assert.Single(results);
        Assert.Contains("### 唯一のシート", results[0]);

        // 分割されていないため、パート表記は付かない
        Assert.DoesNotContain("**Part:**", results[0]);
        Assert.Contains("## Single.xlsx\n", results[0].Replace("\r\n", "\n"));
    }

    [Fact]
    public void GeneratePerFile_NumbersFileNames_WhenSplitIntoParts()
    {
        // Arrange
        var excelPath = Path.Combine(_tempDir, "Book.xlsx");
        CreateExcelFileWithSheets(excelPath, ("シートA", new string('a', 2000)), ("シートB", new string('b', 2000)));
        File.WriteAllText(Path.Combine(_tempDir, "Small.txt"), "小さいファイル");

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false, outputPerFile: true, maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.GeneratePerFile();

        // Assert: 分割されたファイルは連番付き、分割されないファイルは従来どおりの名前になること
        // Split files get a sequence number, while unsplit files keep their original name.
        Assert.Contains(results, r => r.RelativePath == "Book.xlsx.1.md");
        Assert.Contains(results, r => r.RelativePath == "Book.xlsx.2.md");
        Assert.DoesNotContain(results, r => r.RelativePath == "Book.xlsx.md");
        Assert.Contains(results, r => r.RelativePath == "Small.txt.md");
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
    /// テスト用に、指定した「シート名と1セルの値」の組からなる .xlsx ファイルを生成します。
    /// </summary>
    private static void CreateExcelFileWithSheets(string path, params (string Name, string Value)[] sheets)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var sheetElements = workbookPart.Workbook.AppendChild(new Sheets());

        uint sheetId = 1;
        foreach (var (name, value) in sheets)
        {
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            var sheetData = new SheetData();
            worksheetPart.Worksheet = new Worksheet(sheetData);
            sheetData.Append(CreateRow(1, value));
            worksheetPart.Worksheet.Save();

            sheetElements.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = sheetId,
                Name = name
            });
            sheetId++;
        }

        workbookPart.Workbook.Save();
    }

    /// <summary>
    /// テスト用に、1シート・3行（うち1行は空行）の .xlsx ファイルを生成します。
    /// </summary>
    private static void CreateExcelFile(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);

        sheetData.Append(CreateRow(1, "項目", "数量"));
        sheetData.Append(new Row { RowIndex = 2 }); // 空行 / An empty row.
        sheetData.Append(CreateRow(3, "りんご", "3"));

        var sheets = workbookPart.Workbook.AppendChild(new Sheets());
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1,
            Name = "明細シート"
        });

        workbookPart.Workbook.Save();
    }

    /// <summary>
    /// テスト用に、図形（テキストボックス）を持つ2シート構成の .xlsx ファイルを生成します。
    /// 1枚目のシートには埋め込み画像も追加します。
    /// </summary>
    private static void CreateExcelFileWithShapes(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);

        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var sheets = workbookPart.Workbook.AppendChild(new Sheets());

        AppendSheetWithShape(workbookPart, sheets, sheetId: 1, name: "1枚目", cellValue: "1枚目のセル", shapeText: "1枚目の図形テキスト", withImage: true);
        AppendSheetWithShape(workbookPart, sheets, sheetId: 2, name: "2枚目", cellValue: "2枚目のセル", shapeText: "2枚目の図形テキスト", withImage: false);

        workbookPart.Workbook.Save();
    }

    /// <summary>
    /// 1行のセルと1つのテキストボックスを持つシートをブックへ追加します。
    /// </summary>
    private static void AppendSheetWithShape(WorkbookPart workbookPart, Sheets sheets, uint sheetId, string name, string cellValue, string shapeText, bool withImage)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        worksheetPart.Worksheet = new Worksheet(sheetData);
        sheetData.Append(CreateRow(1, cellValue));

        var drawingsPart = worksheetPart.AddNewPart<DrawingsPart>();
        drawingsPart.WorksheetDrawing = new Xdr.WorksheetDrawing(
            new Xdr.TwoCellAnchor(
                new Xdr.FromMarker(new Xdr.ColumnId("0"), new Xdr.ColumnOffset("0"), new Xdr.RowId("0"), new Xdr.RowOffset("0")),
                new Xdr.ToMarker(new Xdr.ColumnId("2"), new Xdr.ColumnOffset("0"), new Xdr.RowId("2"), new Xdr.RowOffset("0")),
                new Xdr.Shape(
                    new Xdr.NonVisualShapeProperties(
                        new Xdr.NonVisualDrawingProperties { Id = 2U, Name = "TextBox 1" },
                        new Xdr.NonVisualShapeDrawingProperties()),
                    new Xdr.ShapeProperties(),
                    new Xdr.TextBody(
                        new A.BodyProperties(),
                        new A.Paragraph(new A.Run(new A.RunProperties { Language = "ja-JP" }, new A.Text(shapeText))))),
                new Xdr.ClientData()));
        drawingsPart.WorksheetDrawing.Save();

        if (withImage)
        {
            // 1x1ピクセルのPNG（OCRは行わないため内容は問わない）
            var imagePart = drawingsPart.AddImagePart(ImagePartType.Png);
            using var imageStream = new MemoryStream(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="));
            imagePart.FeedData(imageStream);
        }

        // シートXMLから図形パーツへの参照を張る（sheetData の後ろに置く必要がある）
        worksheetPart.Worksheet.Append(new Drawing { Id = worksheetPart.GetIdOfPart(drawingsPart) });
        worksheetPart.Worksheet.Save();

        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = sheetId,
            Name = name
        });
    }

    private static Row CreateRow(uint rowIndex, params string[] values)
    {
        var row = new Row { RowIndex = rowIndex };
        for (int i = 0; i < values.Length; i++)
        {
            // 共有文字列テーブルを使わずに済むよう、インライン文字列でセルを作成する
            row.Append(new Cell
            {
                CellReference = $"{(char)('A' + i)}{rowIndex}",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(new Text(values[i]))
            });
        }
        return row;
    }
}
