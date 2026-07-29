using System;
using System.Globalization;
using System.Linq;
using ProjectAnalyzer.Core;
using ProjectAnalyzer.Core.Models;
using ProjectAnalyzer.Core.Utils;

// このファイルはアプリケーションのエントリーポイントです。
// This file is the application's entry point.
// コマンドライン引数を解釈し、分析処理を初期化して実行します。
// It parses command-line arguments, initializes, and runs the analysis process.

// --- 設定 ---
// --- Settings ---
// 1. 引数の中からフラグが含まれているか確認する
bool omitCodeBlockTicks = args.Contains("--no-codeblock");
bool outputPerFile = args.Contains("--per-file");
bool sanitizeHtmlTags = args.Contains("--sanitize-html");
bool removeIndent = args.Contains("--remove-indent");
bool enableOcr = args.Contains("--enable-ocr");

// 2. 出力1ファイルあたりのサイズ上限(MB)を取得する。"--max-size 8" と "--max-size=8" の両方に対応する。
//    Read the per-file output size limit in MB. Both "--max-size 8" and "--max-size=8" are accepted.
long maxOutputSize = AnalyzerSettings.DefaultMaxOutputSize;
int maxSizeValueIndex = -1;

for (int i = 0; i < args.Length; i++)
{
    string? rawValue = null;

    if (args[i] == "--max-size" && i + 1 < args.Length)
    {
        rawValue = args[i + 1];
        // 値として消費した引数がパスと誤認されないよう、位置を控えておく
        // Remember the consumed argument so that it is not mistaken for a path.
        maxSizeValueIndex = i + 1;
    }
    else if (args[i].StartsWith("--max-size=", StringComparison.Ordinal))
    {
        rawValue = args[i].Substring("--max-size=".Length);
    }

    if (rawValue == null) continue;

    if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double megaBytes) && megaBytes > 0)
    {
        maxOutputSize = (long)(megaBytes * 1024 * 1024);
    }
    else
    {
        Console.WriteLine($"⚠️ Invalid --max-size value '{rawValue}'. Falling back to the default (4MB).");
    }

    break;
}

// 3. フラグ（"--" で始まるもの）とその値以外の引数をパスとして抽出する
var pathArgs = args
    .Where((a, i) => !a.StartsWith("--") && i != maxSizeValueIndex)
    .ToArray();

// 4. パスの引数を割り当てる
string projectPath = pathArgs.Length > 0 ? pathArgs[0] : ".";
string outputPath = pathArgs.Length > 1 ? pathArgs[1] : "output";

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
        omitCodeBlockTicks: omitCodeBlockTicks,
        outputPerFile: outputPerFile,
        sanitizeHtmlTags: sanitizeHtmlTags,
        removeIndent: removeIndent,
        enableOcr: enableOcr,
        maxOutputSize: maxOutputSize
    );

    Console.WriteLine("--- Project Analyzer ---");
    Console.WriteLine($"🔍 Project Path: {settings.ProjectPath}");
    Console.WriteLine($"📂 Output Path:  {settings.OutputPath}");
    Console.WriteLine($"📏 Max Size:     {settings.MaxOutputSize / 1024.0 / 1024.0:0.##} MB per file");
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