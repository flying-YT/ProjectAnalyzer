using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Utils;
using Xunit;
using System;
using System.IO;

namespace ProjectAnalyzer.Core.Tests;

public class AnalyzerTests : IDisposable
{
    private readonly string _tempProjectDir;
    private readonly string _tempOutputDir;

    public AnalyzerTests()
    {
        _tempProjectDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _tempOutputDir = Path.Combine(_tempProjectDir, "output");
        Directory.CreateDirectory(_tempProjectDir);

        File.WriteAllText(Path.Combine(_tempProjectDir, "TestFile.cs"), "public class Test {}");
        
        var subDir = Path.Combine(_tempProjectDir, "SubDir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Readme.md"), "# Hello");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempProjectDir))
        {
            Directory.Delete(_tempProjectDir, true);
        }
    }

    [Fact]
    public void Analyze_GeneratesCorrectResultInMemory_WhenOutputToFileIsFalse()
    {
        // Arrange
        var settings = SettingsLoader.Load(_tempProjectDir, _tempOutputDir, outputToFile: false, omitCodeBlockTicks: false);
        using var analyzer = new Analyzer(settings);

        // Act
        var result = analyzer.Analyze();

        // Assert
        Assert.NotNull(result);
        Assert.Contains("TestFile.cs", result.ProjectTree);
        Assert.Single(result.ProjectContexts);
        Assert.Contains("public class Test {}", result.ProjectContexts[0]);
        
        // OutputToFileがfalseなので、出力先フォルダは作られない
        Assert.False(Directory.Exists(_tempOutputDir));
    }

    // ↓↓↓ ここから追加 ↓↓↓
    [Fact]
    public void Analyze_CreatesMarkdownFiles_WhenOutputToFileIsTrue()
    {
        // Arrange
        // OutputToFileをtrueにして実行
        var settings = SettingsLoader.Load(_tempProjectDir, _tempOutputDir, outputToFile: true, omitCodeBlockTicks: false);
        using var analyzer = new Analyzer(settings);

        // Act
        var result = analyzer.Analyze();

        // Assert
        Assert.True(Directory.Exists(_tempOutputDir)); // 出力フォルダが作成されたか

        string treeFilePath = Path.Combine(_tempOutputDir, "00_ProjectTree.md");
        string contextFilePath = Path.Combine(_tempOutputDir, "01_ProjectContext.md");

        Assert.True(File.Exists(treeFilePath)); // ツリーファイルが生成されたか
        Assert.True(File.Exists(contextFilePath)); // コンテキストファイルが生成されたか

        // 生成されたファイルの中身の検証
        string treeContent = File.ReadAllText(treeFilePath);
        Assert.Contains("TestFile.cs", treeContent);

        string contextContent = File.ReadAllText(contextFilePath);
        Assert.Contains("public class Test {}", contextContent);
    }
}