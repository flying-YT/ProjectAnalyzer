using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

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
