using System;
using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;

// このファイルはアプリケーションのエントリーポイントです。
// This file is the application's entry point.
// コマンドライン引数を解釈し、分析処理を初期化して実行します。
// It parses command-line arguments, initializes, and runs the analysis process.

// --- 設定 ---
// --- Settings ---
// 1. 引数の中から "--no-codeblock" フラグが含まれているか確認する
bool omitCodeBlockTicks = args.Contains("--no-codeblock");

// 2. フラグ（"--" で始まるもの）以外の引数をパスとして抽出する
var pathArgs = args.Where(a => !a.StartsWith("--")).ToArray();

// 3. パスの引数を割り当てる
string projectPath = args.Length > 0 ? args[0] : ".";
string outputPath = args.Length > 1 ? args[1] : "output";

// --- メイン処理 ---
// --- Main Process ---
try
{
    // 1. 設定の読み込み
    // 1. Load settings
    var settings = SettingsLoader.Load(
        projectPath, 
        outputPath, 
        outputToFile: true, 
        omitCodeBlockTicks: omitCodeBlockTicks
    );

    Console.WriteLine("--- Project Analyzer ---");
    Console.WriteLine($"🔍 Project Path: {settings.ProjectPath}");
    Console.WriteLine($"📂 Output Path:  {settings.OutputPath}");
    Console.WriteLine("------------------------\n");

    // 2. 分析の実行
    // 2. Run analysis
    using var analyzer = new Analyzer(settings);
    AnalyzerResult result = analyzer.Analyze();

    Console.WriteLine("✅ Analysis complete!");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n❌ An unhandled error occurred: {ex.Message}");
    Console.ResetColor();
}