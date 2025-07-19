public static class SettingsLoader
{
    private const string IgnoreFileName = ".projectanalyzerignore";

    public static AnalyzerSettings Load(string projectPath, string outputPath)
    {
        var ignoreList = new HashSet<string>();

        // デフォルトの除外対象
        ignoreList.Add("bin");
        ignoreList.Add("obj");
        ignoreList.Add(".vs");
        ignoreList.Add(".git");
        ignoreList.Add(Path.GetFileName(Path.GetFullPath(outputPath))); // 出力フォルダ自体
        ignoreList.Add(IgnoreFileName);

        // 設定ファイルから読み込み
        string ignoreFilePath = Path.Combine(Path.GetFullPath(projectPath), IgnoreFileName);
        if (File.Exists(ignoreFilePath))
        {
            var lines = File.ReadAllLines(ignoreFilePath);
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                {
                    ignoreList.Add(line.Trim());
                }
            }
        }

        return new AnalyzerSettings(projectPath, outputPath, ignoreList);
    }
}