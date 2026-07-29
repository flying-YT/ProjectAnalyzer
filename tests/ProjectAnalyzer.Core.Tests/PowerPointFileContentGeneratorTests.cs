using ProjectAnalyzer.Core.Generators;
using ProjectAnalyzer.Core.Models;
using Xunit;
using System;
using System.Collections.Generic;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace ProjectAnalyzer.Core.Tests;

/// <summary>
/// PowerPointファイル(.pptx)の読み込みに関するテストです。
/// Presentation名前空間の型名が他のOffice形式と衝突するため、テストクラスを分けています。
/// </summary>
public class PowerPointFileContentGeneratorTests : IDisposable
{
    private readonly string _tempDir;

    public PowerPointFileContentGeneratorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Generate_ReadsPowerPointFile_WithSlideHeadingsInOrder()
    {
        // Arrange
        var pptxPath = Path.Combine(_tempDir, "Deck.pptx");
        CreatePowerPointFile(pptxPath, "1枚目のテキスト", "2枚目のテキスト");

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false);
        var generator = new FileContentGenerator(settings);

        // Act
        var content = string.Concat(generator.Generate()).Replace("\r\n", "\n");

        // Assert: スライドごとに見出しとテキストが出力されること
        Assert.Contains("### Slide 1", content);
        Assert.Contains("1枚目のテキスト", content);
        Assert.Contains("### Slide 2", content);
        Assert.Contains("2枚目のテキスト", content);

        // スライドの順序が保たれていること
        int firstSlideIndex = content.IndexOf("### Slide 1", StringComparison.Ordinal);
        int secondSlideIndex = content.IndexOf("### Slide 2", StringComparison.Ordinal);
        Assert.True(firstSlideIndex >= 0 && firstSlideIndex < secondSlideIndex, "スライドの順序が保たれていません。");
    }

    [Fact]
    public void Generate_SplitsPowerPointIntoParts_PerSlide_WhenExceedingMaxOutputSize()
    {
        // Arrange: それぞれ単体でしきい値を超えるスライドを2枚持つ .pptx を作成する
        // Create a .pptx with two slides that each exceed the threshold on their own.
        var pptxPath = Path.Combine(_tempDir, "Deck.pptx");
        CreatePowerPointFile(pptxPath, new string('a', 2000), new string('b', 2000));

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false, omitCodeBlockTicks: true, maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: スライド単位で別のコンテキストへ分割されること
        Assert.Equal(2, results.Count);
        Assert.Contains("### Slide 1", results[0]);
        Assert.DoesNotContain("### Slide 2", results[0]);
        Assert.Contains("### Slide 2", results[1]);

        // どのパートにもファイル名と相対パスが再掲されること
        foreach (var part in results)
        {
            Assert.Contains("## Deck.pptx (", part);
            Assert.Contains("**Relative Path:** `Deck.pptx`", part);
        }
    }

    [Fact]
    public void Generate_DoesNotSplitPowerPoint_WhenItHasOnlyOneSlide()
    {
        // Arrange: しきい値を大きく超えるが、スライドが1枚しかない .pptx を作成する
        // Create a .pptx that far exceeds the threshold but has only one slide.
        var pptxPath = Path.Combine(_tempDir, "Single.pptx");
        CreatePowerPointFile(pptxPath, new string('a', 5000));

        var settings = new AnalyzerSettings(_tempDir, "", new HashSet<string>(), outputToFile: false, omitCodeBlockTicks: true, maxOutputSize: 1000);
        var generator = new FileContentGenerator(settings);

        // Act
        var results = generator.Generate();

        // Assert: 分割の境界が無いため、しきい値を超えたまま1つにまとまること
        Assert.Single(results);
        Assert.DoesNotContain("**Part:**", results[0]);
    }

    /// <summary>
    /// テスト用に、指定したテキストを1つずつ持つスライドからなる .pptx ファイルを生成します。
    /// </summary>
    private static void CreatePowerPointFile(string path, params string[] slideTexts)
    {
        using var document = PresentationDocument.Create(path, PresentationDocumentType.Presentation);

        var presentationPart = document.AddPresentationPart();
        presentationPart.Presentation = new Presentation();

        var slideIdList = new SlideIdList();
        uint slideId = 256U;

        foreach (var text in slideTexts)
        {
            var slidePart = presentationPart.AddNewPart<SlidePart>();
            slidePart.Slide = CreateSlide(text);

            slideIdList.Append(new SlideId
            {
                Id = slideId++,
                RelationshipId = presentationPart.GetIdOfPart(slidePart)
            });
        }

        presentationPart.Presentation.SlideIdList = slideIdList;
        presentationPart.Presentation.Save();
    }

    private static Slide CreateSlide(string text) =>
        new Slide(
            new CommonSlideData(
                new ShapeTree(
                    new NonVisualGroupShapeProperties(
                        new NonVisualDrawingProperties { Id = 1U, Name = "" },
                        new NonVisualGroupShapeDrawingProperties(),
                        new ApplicationNonVisualDrawingProperties()),
                    new GroupShapeProperties(),
                    new Shape(
                        new NonVisualShapeProperties(
                            new NonVisualDrawingProperties { Id = 2U, Name = "TextBox" },
                            new NonVisualShapeDrawingProperties(),
                            new ApplicationNonVisualDrawingProperties()),
                        new ShapeProperties(),
                        new TextBody(
                            new A.BodyProperties(),
                            new A.ListStyle(),
                            new A.Paragraph(new A.Run(new A.Text(text))))))));
}
