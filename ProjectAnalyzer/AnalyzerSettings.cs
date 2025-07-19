public class AnalyzerSettings
{
    public string ProjectPath { get; }
    public string OutputPath { get; }
    public ISet<string> IgnoreList { get; }

    public AnalyzerSettings(string projectPath, string outputPath, ISet<string> ignoreList)
    {
        ProjectPath = Path.GetFullPath(projectPath);
        OutputPath = Path.GetFullPath(outputPath);
        IgnoreList = ignoreList;
    }
}