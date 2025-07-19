// --- 設定 ---
string projectPath = args.Length > 0 ? args[0] : ".";
string outputPath = args.Length > 1 ? args[1] : "output";

// --- メイン処理 ---
try
{
    // 1. 設定の読み込み
    var settings = SettingsLoader.Load(projectPath, outputPath);

    Console.WriteLine("--- Project Analyzer ---");
    Console.WriteLine($"🔍 Project Path: {settings.ProjectPath}");
    Console.WriteLine($"📂 Output Path:  {settings.OutputPath}");
    Console.WriteLine("------------------------\n");

    // 2. 分析の実行
    var analyzer = new ProjectAnalyzer(settings);
    analyzer.Analyze();

    Console.WriteLine("✅ Analysis complete!");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n❌ An unhandled error occurred: {ex.Message}");
    Console.ResetColor();
}