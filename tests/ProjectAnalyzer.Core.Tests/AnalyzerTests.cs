using ProjectAnalyzer.Core;
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
        // ダミーのプロジェクトフォルダ構造を作成
        _tempProjectDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _tempOutputDir = Path.Combine(_tempProjectDir, "output");
        Directory.CreateDirectory(_tempProjectDir);

        // ダミーファイルの作成
        File.WriteAllText(Path.Combine(_tempProjectDir, "TestFile.cs"), "public class Test {}");
        
        var subDir = Path.Combine(_tempProjectDir, "SubDir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Readme.md"), "# Hello");
    }

    public void Dispose()
    {
        // お掃除
        if (Directory.Exists(_tempProjectDir))
        {
            Directory.Delete(_tempProjectDir, true);
        }
    }

    [Fact]
    public void Analyze_GeneratesCorrectResultInMemory_WhenOutputToFileIsFalse()
    {
        // Arrange
        // メモリ上に保持する設定 (OutputToFile: false)
        var settings = SettingsLoader.Load(_tempProjectDir, _tempOutputDir, outputToFile: false, omitCodeBlockTicks: false);
        var analyzer = new Analyzer(settings);

        // Act
        var result = analyzer.Analyze();

        // Assert
        Assert.NotNull(result);
        
        // ツリー構造が取得できているか
        Assert.Contains("TestFile.cs", result.ProjectTree);
        Assert.Contains("SubDir", result.ProjectTree);
        Assert.Contains("Readme.md", result.ProjectTree);

        // ファイルコンテンツが生成されているか
        Assert.Single(result.ProjectContexts); // 4MB以下なので1つにまとまっているはず
        Assert.Contains("public class Test {}", result.ProjectContexts[0]);
        Assert.Contains("# Hello", result.ProjectContexts[0]);

        // OutputToFileがfalseなので、実際のフォルダやファイルが作られていないか
        Assert.False(Directory.Exists(_tempOutputDir));
    }
}