public class ProjectAnalyzer
{
    private readonly AnalyzerSettings _settings;
    private readonly TreeGenerator _treeGenerator;
    private readonly FileContentGenerator _fileContentGenerator;

    public ProjectAnalyzer(AnalyzerSettings settings)
    {
        _settings = settings;
        _treeGenerator = new TreeGenerator(settings);
        _fileContentGenerator = new FileContentGenerator(settings);
    }

    public void Analyze()
    {
        PrepareOutputDirectory();

        Console.WriteLine("🌳 Generating folder tree...");
        string tree = _treeGenerator.Generate();
        File.WriteAllText(Path.Combine(_settings.OutputPath, "00_ProjectTree.md"), $"# 🌳 Project Folder Tree\n\n```\n{tree}\n```");
        Console.WriteLine("   -> Success: 00_ProjectTree.md\n");

        Console.WriteLine("📄 Generating file contents...");
        _fileContentGenerator.Generate();
        Console.WriteLine("   -> Success: All files have been processed.\n");
    }

    private void PrepareOutputDirectory()
    {
        if (Directory.Exists(_settings.OutputPath))
        {
            Directory.Delete(_settings.OutputPath, true);
        }
        Directory.CreateDirectory(_settings.OutputPath);
    }
}