// このファイルはアプリケーションのエントリーポイントです。
// This file is the application's entry point.
// コマンドライン引数を解釈し、分析処理を初期化して実行します。
// It parses command-line arguments, initializes, and runs the analysis process.

// --- 設定 ---
// --- Settings ---
string projectPath = args.Length > 0 ? args[0] : ".";
string outputPath = args.Length > 1 ? args[1] : "output";

// --- メイン処理 ---
// --- Main Process ---
try
{
    // 1. 設定の読み込み
    // 1. Load settings
    var settings = SettingsLoader.Load(projectPath, outputPath);

    Console.WriteLine("--- Project Analyzer ---");
    Console.WriteLine($"🔍 Project Path: {settings.ProjectPath}");
    Console.WriteLine($"📂 Output Path:  {settings.OutputPath}");
    Console.WriteLine("------------------------\n");

    // 2. 分析の実行
    // 2. Run analysis
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