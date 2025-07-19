using System.Text;

public class TreeGenerator
{
    private readonly AnalyzerSettings _settings;

    public TreeGenerator(AnalyzerSettings settings)
    {
        _settings = settings;
    }

    public string Generate()
    {
        var sb = new StringBuilder();
        var rootDirInfo = new DirectoryInfo(_settings.ProjectPath);
        sb.AppendLine(rootDirInfo.Name);
        GenerateRecursive(rootDirInfo, "", sb, true);
        return sb.ToString();
    }

    private void GenerateRecursive(DirectoryInfo directory, string indent, StringBuilder sb, bool isLast)
    {
        var subDirectories = directory.GetDirectories()
                                      .Where(d => !_settings.IgnoreList.Contains(d.Name))
                                      .OrderBy(d => d.Name)
                                      .ToList();
        var files = directory.GetFiles()
                             .Where(f => !_settings.IgnoreList.Contains(f.Name))
                             .OrderBy(f => f.Name)
                             .ToList();

        string newIndent = indent + (isLast ? "    " : "│   ");

        for (int i = 0; i < subDirectories.Count; i++)
        {
            var subDir = subDirectories[i];
            bool isLastEntry = (i == subDirectories.Count - 1) && (files.Count == 0);
            sb.AppendLine($"{indent}└── {(isLastEntry ? "└── " : "├── ")}{subDir.Name}");
            GenerateRecursive(subDir, newIndent, sb, isLastEntry);
        }

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            bool isLastEntry = (i == files.Count - 1);
            sb.AppendLine($"{indent}└── {(isLastEntry ? "└── " : "├── ")}{file.Name}");
        }
    }
}