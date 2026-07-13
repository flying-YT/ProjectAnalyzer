using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;

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
}