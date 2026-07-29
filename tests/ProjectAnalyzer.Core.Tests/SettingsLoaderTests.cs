using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Models;
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

    [Fact]
    public void Load_UsesDefaultMaxOutputSize_WhenNotSpecified()
    {
        // Act
        var settings = SettingsLoader.Load(_tempPath, "output", false, false);

        // Assert: 既定のしきい値（4MB）が使われること
        Assert.Equal(AnalyzerSettings.DefaultMaxOutputSize, settings.MaxOutputSize);
    }

    [Fact]
    public void Load_PassesThroughMaxOutputSize()
    {
        // Act
        var settings = SettingsLoader.Load(_tempPath, "output", false, false, maxOutputSize: 8 * 1024 * 1024);

        // Assert
        Assert.Equal(8 * 1024 * 1024, settings.MaxOutputSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Load_FallsBackToDefaultMaxOutputSize_WhenValueIsNotPositive(long invalidSize)
    {
        // Act: 0以下だと分割が無限に発生するため、既定値へ丸められること
        // Values of zero or less would cause endless splitting, so they fall back to the default.
        var settings = SettingsLoader.Load(_tempPath, "output", false, false, maxOutputSize: invalidSize);

        // Assert
        Assert.Equal(AnalyzerSettings.DefaultMaxOutputSize, settings.MaxOutputSize);
    }
}