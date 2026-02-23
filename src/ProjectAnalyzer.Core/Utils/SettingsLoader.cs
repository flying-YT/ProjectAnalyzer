using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProjectAnalyzer.Core.Models;

namespace ProjectAnalyzer.Core.Utils;
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
    /// <param name="outputToFile">ファイル出力を行うかどうかのフラグ。/ A flag indicating whether to perform file output.</param>
    /// <param name="omitCodeBlockTicks">Markdownのコードブロック(```)を省略するかどうかのフラグ。/ A flag indicating whether to omit Markdown code blocks (```).</param>
    /// <returns>読み込まれた設定情報を含む `AnalyzerSettings` インスタンス。/ An `AnalyzerSettings` instance containing the loaded configuration.</returns>
    public static AnalyzerSettings Load(string projectPath, string outputPath, bool outputToFile = true, bool omitCodeBlockTicks = false)   
    {
        string targetPath = projectPath;
        string? tempCloneDir = null;

        // URLの場合は Git Clone を実行する
        if (projectPath.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            string cloneUrl = projectPath;
            string branchName = string.Empty;

            // URLに "/tree/" が含まれている場合、リポジトリURLとブランチ名に分割する
            const string treeIndicator = "/tree/";
            int treeIndex = projectPath.IndexOf(treeIndicator, StringComparison.OrdinalIgnoreCase);
            
            if (treeIndex > 0)
            {
                cloneUrl = projectPath.Substring(0, treeIndex); // ベースのリポジトリURL
                branchName = projectPath.Substring(treeIndex + treeIndicator.Length).TrimEnd('/'); // ブランチ名
            }

            // リポジトリ名を抽出 (ツリー表示を見やすくするため)
            string repoName = cloneUrl.TrimEnd('/').Split('/').Last();
            if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repoName = repoName.Substring(0, repoName.Length - 4);
            }
            
            tempCloneDir = Path.Combine(Path.GetTempPath(), "ProjectAnalyzer", Guid.NewGuid().ToString(), repoName);
            Directory.CreateDirectory(tempCloneDir);

            // PATによる認証の組み込み
            string authCloneUrl = cloneUrl;
            string? pat = Environment.GetEnvironmentVariable("GITHUB_PAT");
            if (!string.IsNullOrEmpty(pat))
            {
                var uriBuilder = new UriBuilder(cloneUrl) { UserName = pat };
                authCloneUrl = uriBuilder.ToString();
                Console.WriteLine("🔒 Using Personal Access Token for authentication.");
            }

            // ログ出力の切り替え
            if (string.IsNullOrEmpty(branchName))
            {
                Console.WriteLine($"📥 Cloning repository from {cloneUrl}...");
            }
            else
            {
                Console.WriteLine($"📥 Cloning repository from {cloneUrl} (Branch: {branchName})...");
            }

            // git clone コマンドの引数を動的に組み立てる
            string gitArgs = "clone --depth 1";
            if (!string.IsNullOrEmpty(branchName))
            {
                // ブランチ指定がある場合は -b オプションを追加
                gitArgs += $" -b \"{branchName}\"";
            }
            gitArgs += $" \"{authCloneUrl}\" \"{tempCloneDir}\"";

            var processInfo = new ProcessStartInfo("git", gitArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                string error = process?.StandardError.ReadToEnd() ?? "Unknown error";
                throw new Exception($"Git clone failed: {error}\nCommand Executed: git {gitArgs.Replace(authCloneUrl, cloneUrl)}"); // ログにPATが漏れないように置換
            }

            // 分析対象のパスをクローンした一時フォルダに差し替える
            targetPath = tempCloneDir;
        }

        // --- 以下は既存のロジック ---
        var ignoreList = new HashSet<string>
        {
            "bin", "obj", ".vs", ".git"
        };
        
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            ignoreList.Add(Path.GetFileName(Path.GetFullPath(outputPath)));
        }
        ignoreList.Add(IgnoreFileName);

        string ignoreFilePath = Path.Combine(Path.GetFullPath(targetPath), IgnoreFileName);
        if (File.Exists(ignoreFilePath))
        {
            foreach (var line in File.ReadAllLines(ignoreFilePath))
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                {
                    ignoreList.Add(line.Trim());
                }
            }
        }

        return new AnalyzerSettings(targetPath, outputPath, ignoreList, outputToFile, omitCodeBlockTicks, tempCloneDir);
    }
}