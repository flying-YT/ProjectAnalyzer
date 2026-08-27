using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ProjectAnalyzer.Core.Tests;

/// <summary>
/// 内容をテキストとして抽出できないファイル（PDFや画像などのバイナリ）の扱いを検証します。
/// Verifies how files whose content cannot be extracted as text (binaries such as PDFs and images)
/// are handled.
/// </summary>
public class SkippedFileContentGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public SkippedFileContentGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private AnalyzerSettings CreateSettings(string outputPath = "", bool outputToFile = false)
        => new AnalyzerSettings(
            _tempDir,
            outputPath,
            new HashSet<string>(),
            outputToFile: outputToFile,
            omitCodeBlockTicks: true);

    [Fact]
    public void Generate_SkipsBinaryFileContent_AndKeepsTextFiles()
    {
        // Arrange: バイナリ(PDF)とテキスト(ソースコード)を並べる
        // Place a binary (PDF) next to a text file (source code).
        File.WriteAllBytes(Path.Combine(_tempDir, "Spec.pdf"), CreatePdfBytes());
        File.WriteAllText(Path.Combine(_tempDir, "Code.cs"), "public class A {}");

        var generator = new FileContentGenerator(CreateSettings());

        // Act
        var content = string.Concat(generator.Generate());

        // Assert: PDFは存在を示すプレースホルダになり、中身は出力されないこと
        Assert.Contains("## Spec.pdf", content);
        Assert.Contains("Skipped: unsupported binary file (.pdf", content);
        Assert.DoesNotContain("%PDF-", content);

        // 文字化けの元になる置換文字が混ざっていないこと
        Assert.DoesNotContain("�", content);

        // テキストファイルは従来どおり抽出されること
        Assert.Contains("public class A {}", content);

        // スキップしたファイルが呼び出し側へ通知されること
        Assert.Equal(new[] { Path.Combine(_tempDir, "Spec.pdf") }, generator.SkippedBinaryFiles);
    }

    [Fact]
    public void Generate_SkipsBinaryFile_WithUnknownExtension()
    {
        // Arrange: 拡張子からは判断できないが、NULバイトを含むファイル
        // A file whose extension is inconclusive but whose content contains NUL bytes.
        File.WriteAllBytes(Path.Combine(_tempDir, "Data.xyz"), new byte[] { 0x01, 0x00, 0x02, 0x03 });

        var generator = new FileContentGenerator(CreateSettings());

        // Act
        var content = string.Concat(generator.Generate());

        // Assert
        Assert.Contains("Skipped: unsupported binary file (.xyz", content);
        Assert.Single(generator.SkippedBinaryFiles);
    }

    [Fact]
    public void Generate_KeepsTextFile_WithBomEncodedContent()
    {
        // Arrange: UTF-16のテキストはNULバイトを含むが、BOMがあるのでテキストとして扱われる
        // UTF-16 text contains NUL bytes, but its BOM identifies it as text.
        File.WriteAllText(Path.Combine(_tempDir, "Utf16.txt"), "UTF-16のテキスト", new UnicodeEncoding(false, true));

        var generator = new FileContentGenerator(CreateSettings());

        // Act
        var content = string.Concat(generator.Generate());

        // Assert
        Assert.Contains("UTF-16のテキスト", content);
        Assert.Empty(generator.SkippedBinaryFiles);
    }

    [Fact]
    public void Generate_KeepsOfficeFiles_EvenThoughTheyArePackages()
    {
        // Arrange: Office形式は中身がZIPだが、専用の抽出処理があるためスキップされてはならない
        // Office formats are ZIP packages, but they must not be skipped since an extractor exists.
        string excelPath = Path.Combine(_tempDir, "Book.xlsx");
        File.WriteAllBytes(excelPath, CreateMinimalZipBytes());

        var generator = new FileContentGenerator(CreateSettings());

        // Act
        generator.Generate();

        // Assert: 読み込みに失敗しても、バイナリとしてスキップされてはいないこと
        Assert.Empty(generator.SkippedBinaryFiles);
    }

    [Fact]
    public void Analyze_CopiesSkippedFilesToOutput_PreservingDirectoryStructure()
    {
        // Arrange: サブフォルダを含む構成でバイナリを配置する
        // Place binaries in a layout that includes a subfolder.
        string docsDir = Path.Combine(_tempDir, "docs");
        Directory.CreateDirectory(docsDir);

        File.WriteAllBytes(Path.Combine(docsDir, "Spec.pdf"), CreatePdfBytes());
        File.WriteAllBytes(Path.Combine(_tempDir, "Logo.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x01 });
        File.WriteAllText(Path.Combine(_tempDir, "Code.cs"), "public class A {}");

        string outputPath = Path.Combine(_tempDir, "output");
        using var analyzer = new Analyzer(CreateSettings(outputPath, outputToFile: true));

        // Act
        var result = analyzer.Analyze();

        // Assert: 原本が階層構造を保ったままコピーされること
        string copiedRoot = Path.Combine(outputPath, AnalyzerSettings.SkippedFilesDirectoryName);
        Assert.True(File.Exists(Path.Combine(copiedRoot, "docs", "Spec.pdf")), "PDFがコピーされていません。");
        Assert.True(File.Exists(Path.Combine(copiedRoot, "Logo.png")), "画像がコピーされていません。");

        // コピーされた内容が原本と同じであること
        Assert.Equal(CreatePdfBytes(), File.ReadAllBytes(Path.Combine(copiedRoot, "docs", "Spec.pdf")));

        // 抽出できたファイルはコピー対象にならないこと
        Assert.False(File.Exists(Path.Combine(copiedRoot, "Code.cs")), "テキストファイルがコピーされています。");

        // 分析結果からもスキップしたファイルを取得できること
        Assert.Equal(
            new[] { Path.Combine("docs", "Spec.pdf"), "Logo.png" }.OrderBy(p => p),
            result.SkippedFiles.OrderBy(p => p));
    }

    [Fact]
    public void Analyze_DoesNotCopySkippedFiles_WhenFileOutputIsDisabled()
    {
        // Arrange
        File.WriteAllBytes(Path.Combine(_tempDir, "Spec.pdf"), CreatePdfBytes());

        string outputPath = Path.Combine(_tempDir, "output");
        using var analyzer = new Analyzer(CreateSettings(outputPath, outputToFile: false));

        // Act
        var result = analyzer.Analyze();

        // Assert: メモリ上で受け取るだけの場合はコピーもフォルダ生成も行わないこと
        Assert.False(Directory.Exists(outputPath), "ファイル出力が無効なのに出力フォルダが作られています。");
        Assert.Equal(new[] { "Spec.pdf" }, result.SkippedFiles);
    }

    /// <summary>
    /// テスト用の最小限のPDFバイト列です。ヘッダーに続けてバイナリらしいバイトを並べています。
    /// </summary>
    private static byte[] CreatePdfBytes()
        => Encoding.ASCII.GetBytes("%PDF-1.7\n")
            .Concat(new byte[] { 0x00, 0x01, 0x02, 0xFF, 0xFE })
            .ToArray();

    /// <summary>
    /// テスト用の空のZIPバイト列です（End of central directory レコードのみ）。
    /// </summary>
    private static byte[] CreateMinimalZipBytes()
        => new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
}
