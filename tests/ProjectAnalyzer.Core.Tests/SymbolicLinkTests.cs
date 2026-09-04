using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;

namespace ProjectAnalyzer.Core.Tests;

/// <summary>
/// シンボリックリンクを辿らないことを検証するテストです。
/// リンクを辿ると、解析対象フォルダの外にあるファイルの内容が出力へ混入し、
/// ループするリンクでは走査が終わらなくなります。
/// Tests verifying that symbolic links are not followed.
/// Following them would leak the content of files outside the analyzed folder into the output, and a
/// link that loops would make the traversal never end.
/// </summary>
public class SymbolicLinkTests : IDisposable
{
    private const string SecretContent = "SECRET_OUTSIDE_THE_PROJECT";

    private readonly string _root;
    private readonly string _projectDir;
    private readonly string _outsideDir;
    private readonly string _outputDir;

    public SymbolicLinkTests()
    {
        // プロジェクトフォルダと、その外側のフォルダを兄弟として用意する
        // Prepare the project folder and an outside folder as siblings.
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _projectDir = Path.Combine(_root, "project");
        _outsideDir = Path.Combine(_root, "outside");
        _outputDir = Path.Combine(_root, "output");

        Directory.CreateDirectory(_projectDir);
        Directory.CreateDirectory(_outsideDir);

        File.WriteAllText(Path.Combine(_projectDir, "Program.cs"), "public class Program {}");
        File.WriteAllText(Path.Combine(_outsideDir, "secret.txt"), SecretContent);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;

        // 読み取り専用にしたファイルが残っていると削除できないため、属性を戻してから削除する。
        // ループするリンクを辿らないよう、再帰はせず対象のフォルダだけを見る。
        // Reset the attributes of any read-only file, otherwise the directory cannot be deleted.
        // It does not recurse, so that a link which loops is never followed.
        foreach (var file in Directory.GetFiles(_outsideDir))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_root, true);
    }

    private AnalyzerSettings LoadSettings(bool outputToFile)
        => SettingsLoader.Load(_projectDir, _outputDir, outputToFile: outputToFile);

    [SymbolicLinkFact]
    public void Analyze_DoesNotReadFileLinkPointingOutsideProject()
    {
        // Arrange
        File.CreateSymbolicLink(Path.Combine(_projectDir, "linked_secret.txt"), Path.Combine(_outsideDir, "secret.txt"));
        using var analyzer = new Analyzer(LoadSettings(outputToFile: false));

        // Act
        var result = analyzer.Analyze();

        // Assert
        // リンク先の内容は読み取られない
        // The content behind the link is not read.
        Assert.DoesNotContain(SecretContent, string.Join("\n", result.ProjectContexts));

        // 存在自体はツリーに残り、辿っていないことが分かる
        // Its presence remains in the tree, showing that it was not followed.
        Assert.Contains("linked_secret.txt [symbolic link, not followed]", result.ProjectTree);
    }

    [SymbolicLinkFact]
    public void Analyze_DoesNotWalkDirectoryLinkPointingOutsideProject()
    {
        // Arrange
        Directory.CreateSymbolicLink(Path.Combine(_projectDir, "linked_dir"), _outsideDir);
        using var analyzer = new Analyzer(LoadSettings(outputToFile: false));

        // Act
        var result = analyzer.Analyze();

        // Assert
        Assert.DoesNotContain(SecretContent, string.Join("\n", result.ProjectContexts));

        // リンクの中身は走査されないため、リンク先のファイル名もツリーに現れない
        // The link is not walked, so the file behind it does not appear in the tree either.
        Assert.Contains("linked_dir [symbolic link, not followed]", result.ProjectTree);
        Assert.DoesNotContain("secret.txt", result.ProjectTree);

        // 通常のファイルはこれまでどおり解析される
        // Ordinary files are still analyzed as before.
        Assert.Contains("public class Program {}", string.Join("\n", result.ProjectContexts));
    }

    [SymbolicLinkFact]
    public void Analyze_DoesNotRepeatContent_WhenDirectoryLinkLoops()
    {
        // Arrange
        // 自分の親を指すリンクを作る（辿ると同じ階層を何度も走査してしまう）
        // Create a link pointing at its own parent, which would re-walk the same levels when followed.
        string subDir = Path.Combine(_projectDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "Loop.cs"), "public class Loop {}");
        Directory.CreateSymbolicLink(Path.Combine(subDir, "up"), _projectDir);

        using var analyzer = new Analyzer(LoadSettings(outputToFile: false));

        // Act
        var result = analyzer.Analyze();

        // Assert
        // ループを辿っていないので、同じファイルが複数回現れることはない
        // The loop is not followed, so the same file never appears more than once.
        Assert.Equal(1, CountOccurrences(result.ProjectTree, "Loop.cs"));

        // 本文は details とコードブロックの2箇所へ出力される仕様のため、見出しの数で重複を数える
        // The body is emitted twice by design (details and code block), so headings are counted instead.
        Assert.Equal(1, CountOccurrences(string.Join("\n", result.ProjectContexts), "## Loop.cs"));
        Assert.Contains("up [symbolic link, not followed]", result.ProjectTree);
    }

    [SymbolicLinkFact]
    public void Analyze_DoesNotCopyBinaryFileLinkPointingOutsideProject()
    {
        // Arrange
        // 内容を抽出できない形式は原本がコピーされるため、リンクの場合はコピーもされないことを確認する
        // Unsupported formats have their original copied, so verify that a link is not copied either.
        string outsidePdf = Path.Combine(_outsideDir, "secret.pdf");
        File.WriteAllText(outsidePdf, SecretContent);
        File.CreateSymbolicLink(Path.Combine(_projectDir, "linked.pdf"), outsidePdf);

        using var analyzer = new Analyzer(LoadSettings(outputToFile: true));

        // Act
        var result = analyzer.Analyze();

        // Assert
        Assert.Empty(result.SkippedFiles);
        Assert.False(File.Exists(Path.Combine(_outputDir, AnalyzerSettings.SkippedFilesDirectoryName, "linked.pdf")));
    }

    [SymbolicLinkFact]
    public void Dispose_DoesNotChangeAttributesOutsideTemporaryDirectory()
    {
        // Arrange
        // 一時クローンフォルダの後始末で、フォルダ外のファイルの属性が変更されないことを確認する
        // Verify that cleaning up the temporary clone folder leaves attributes outside it untouched.
        string tempCloneDir = Path.Combine(_root, "clone");
        Directory.CreateDirectory(tempCloneDir);
        File.WriteAllText(Path.Combine(tempCloneDir, "cloned.cs"), "public class Cloned {}");
        Directory.CreateSymbolicLink(Path.Combine(tempCloneDir, "linked_dir"), _outsideDir);

        string outsideFile = Path.Combine(_outsideDir, "secret.txt");
        File.SetAttributes(outsideFile, FileAttributes.ReadOnly);

        var settings = new AnalyzerSettings(
            tempCloneDir,
            _outputDir,
            new HashSet<string>(),
            outputToFile: false,
            tempClonePath: tempCloneDir);

        // Act
        using (var analyzer = new Analyzer(settings))
        {
            analyzer.Analyze();
        }

        // Assert
        // 一時フォルダは削除され、リンク先のファイルは属性も内容もそのまま残る
        // The temporary folder is deleted, while the linked file keeps its attributes and content.
        Assert.False(Directory.Exists(tempCloneDir));
        Assert.True(File.Exists(outsideFile));
        Assert.True(new FileInfo(outsideFile).IsReadOnly);
    }

    [SymbolicLinkFact]
    public void IsSymbolicLink_DistinguishesLinksFromRealEntries()
    {
        // Arrange
        string realFile = Path.Combine(_projectDir, "Program.cs");
        string fileLink = Path.Combine(_projectDir, "link.cs");
        string directoryLink = Path.Combine(_projectDir, "link_dir");

        File.CreateSymbolicLink(fileLink, realFile);
        Directory.CreateSymbolicLink(directoryLink, _outsideDir);

        // Act & Assert
        Assert.False(SymbolicLinkDetector.IsSymbolicLink(realFile));
        Assert.False(SymbolicLinkDetector.IsSymbolicLink(_projectDir));
        Assert.True(SymbolicLinkDetector.IsSymbolicLink(fileLink));
        Assert.True(SymbolicLinkDetector.IsSymbolicLink(directoryLink));
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
