using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;

namespace ProjectAnalyzer.Core.Tests;

public class TreeGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public TreeGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);

        // テスト用のディレクトリとファイルを作成
        Directory.CreateDirectory(Path.Combine(_tempDir, "FolderA"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "FolderB"));
        File.WriteAllText(Path.Combine(_tempDir, "FolderA", "File1.txt"), "test");
        File.WriteAllText(Path.Combine(_tempDir, "RootFile.txt"), "test");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Generate_CreatesCorrectTreeStructure()
    {
        // Arrange
        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string> { "FolderB" }, false, false);
        var generator = new TreeGenerator(settings);

        // Act
        var result = generator.Generate();

        // Assert
        Assert.Contains("FolderA", result);
        Assert.Contains("File1.txt", result);
        Assert.Contains("RootFile.txt", result);
        
        // FolderBは除外リストに入れたので含まれないはず
        Assert.DoesNotContain("FolderB", result);

        // 枝の記号が含まれているか
        Assert.Contains("├──", result);
        Assert.Contains("└──", result);
    }
}