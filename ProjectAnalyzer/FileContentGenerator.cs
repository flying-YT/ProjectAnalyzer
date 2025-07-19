using System.Text;

public class FileContentGenerator
{
    private readonly AnalyzerSettings _settings;

    public FileContentGenerator(AnalyzerSettings settings)
    {
        _settings = settings;
    }

    public void Generate()
    {
        GenerateRecursive(_settings.ProjectPath);
    }

    private void GenerateRecursive(string currentPath)
    {
        // Files
        foreach (var file in Directory.GetFiles(currentPath))
        {
            if (_settings.IgnoreList.Contains(Path.GetFileName(file))) continue;

            try
            {
                CreateMarkdownForFile(file);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   [Warning] Could not process file '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        // Directories
        foreach (var dir in Directory.GetDirectories(currentPath))
        {
            if (_settings.IgnoreList.Contains(Path.GetFileName(dir))) continue;
            GenerateRecursive(dir);
        }
    }

    private void CreateMarkdownForFile(string filePath)
    {
        string relativePath = Path.GetRelativePath(_settings.ProjectPath, filePath);
        string outputFilePath = Path.Combine(_settings.OutputPath, relativePath + ".md");

        Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath));

        string content = File.ReadAllText(filePath);
        string language = LanguageMapper.GetLanguage(Path.GetExtension(filePath));

        var mdContent = new StringBuilder();
        mdContent.AppendLine($"# {Path.GetFileName(filePath)}");
        mdContent.AppendLine();
        mdContent.AppendLine($"**Relative Path:** `{relativePath}`");
        mdContent.AppendLine();
        mdContent.AppendLine($"```{language}");
        mdContent.AppendLine(content);
        mdContent.AppendLine("```");

        File.WriteAllText(outputFilePath, mdContent.ToString());
    }
}