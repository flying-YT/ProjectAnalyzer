/// <summary>
/// アナライザーの設定を読み込むクラスです。
/// A class for loading analyzer settings.
/// </summary>
public static class SettingsLoader
{
    private const string IgnoreFileName = ".projectanalyzerignore";

    /// <summary>
    /// 設定を読み込み、`AnalyzerSettings` オブジェクトを生成します。
    /// デフォルトの除外リストと `.projectanalyzerignore` ファイルの内容をマージします。
    /// Loads the settings and creates an `AnalyzerSettings` object.
    /// It merges the default ignore list with the contents of the `.projectanalyzerignore` file.
    /// </summary>
    /// <param name="projectPath">分析対象のプロジェクトのパス。/ The path to the project to be analyzed.</param>
    /// <param name="outputPath">分析結果を出力するディレクトリのパス。/ The path to the directory where the analysis results will be output.</param>
    /// <returns>読み込まれた設定情報を含む `AnalyzerSettings` インスタンス。/ An `AnalyzerSettings` instance containing the loaded configuration.</returns>
    public static AnalyzerSettings Load(string projectPath, string outputPath)
    {
        var ignoreList = new HashSet<string>();

        // デフォルトの除外対象
        // Default exclusions
        ignoreList.Add("bin");
        ignoreList.Add("obj");
        ignoreList.Add(".vs");
        ignoreList.Add(".git");
        ignoreList.Add(Path.GetFileName(Path.GetFullPath(outputPath))); // 出力フォルダ自体 / The output folder itself
        ignoreList.Add(IgnoreFileName);

        // 設定ファイルから読み込み
        // Load from the settings file
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