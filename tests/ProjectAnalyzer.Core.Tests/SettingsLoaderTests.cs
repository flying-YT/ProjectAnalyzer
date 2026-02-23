using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Utils;
using Xunit;
using System;
using System.IO;

namespace ProjectAnalyzer.Core.Tests;

public class SettingsLoaderTests : IDisposable
{
    private readonly string _tempPath;

    public SettingsLoaderTests()
    {
        // テスト用のランダムな一時ディレクトリを作成
        _tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempPath);
    }

    public void Dispose()
    {
        // テスト終了後に一時ディレクトリを削除
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [Fact]
    public void Load_AddsDefaultIgnoreItems()
    {
        // Act
        var settings = SettingsLoader.Load(_tempPath, "output", false, false);

        // Assert
        Assert.Contains("bin", settings.IgnoreList);
        Assert.Contains("obj", settings.IgnoreList);
        Assert.Contains(".git", settings.IgnoreList);
    }

    [Fact]
    public void Load_ReadsIgnoreFileCorrectly()
    {
        // Arrange
        var ignoreFile = Path.Combine(_tempPath, ".projectanalyzerignore");
        File.WriteAllText(ignoreFile, "node_modules\n*.tmp\n# コメント行\n");

        // Act
        var settings = SettingsLoader.Load(_tempPath, "output", false, false);

        // Assert
        Assert.Contains("node_modules", settings.IgnoreList); // 指定した除外ファイルが含まれるか
        Assert.Contains("*.tmp", settings.IgnoreList);
        Assert.DoesNotContain("# コメント行", settings.IgnoreList); // コメント行が除外リストに入っていないか
    }
}