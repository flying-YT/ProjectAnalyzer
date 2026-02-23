using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Utils;
using Xunit;

namespace ProjectAnalyzer.Core.Tests;

public class LanguageMapperTests
{
    [Theory]
    [InlineData(".cs", "csharp")]
    [InlineData(".md", "markdown")]
    [InlineData(".ts", "typescript")]
    [InlineData(".UNKNOWN", "")] // 未知の拡張子
    [InlineData(".CS", "csharp")] // 大文字小文字の区別をしないか
    public void GetLanguage_ReturnsCorrectMarkdownLanguage(string extension, string expected)
    {
        // Act (実行)
        var result = LanguageMapper.GetLanguage(extension);

        // Assert (検証)
        Assert.Equal(expected, result);
    }
}