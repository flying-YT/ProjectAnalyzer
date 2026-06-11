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
    /// <param name="outputPerFile">個別にファイルを出力するかどうかのフラグ。/ A flag indicating whether to output files individually.</param>
    /// <param name="sanitizeHtmlTags">HTMLタグを置換するかどうかのフラグ。/ A flag indicating whether to sanitize HTML tags.</param>
    /// <param name="removeIndent">インデントを削除するかどうかのフラグ。/ A flag indicating whether to remove indents.</param>
    /// <returns>読み込まれた設定情報を含む `AnalyzerSettings` インスタンス。/ An `AnalyzerSettings` instance containing the loaded configuration.</returns>
    public static AnalyzerSettings Load(string projectPath, string outputPath, bool outputToFile = true, bool omitCodeBlockTicks = false, bool outputPerFile = false, bool sanitizeHtmlTags = false, bool removeIndent = false, bool enableOcr = false)   
    {
        string targetPath = projectPath;
        string? tempCloneDir = null;

        // URLの場合は Git Clone を実行する
        // Execute Git Clone if it is a URL.
        if (projectPath.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            string cloneUrl = projectPath;
            string branchName = string.Empty;

            // URLに "/tree/" が含まれている場合、リポジトリURLとブランチ名に分割する
            // If the URL contains "/tree/", split it into the repository URL and the branch name.
            const string treeIndicator = "/tree/";
            int treeIndex = projectPath.IndexOf(treeIndicator, StringComparison.OrdinalIgnoreCase);
            
            if (treeIndex > 0)
            {
                cloneUrl = projectPath.Substring(0, treeIndex); // ベースのリポジトリURL
                                                                // Base repository URL
                branchName = projectPath.Substring(treeIndex + treeIndicator.Length).TrimEnd('/'); // ブランチ名
                                                                                                   // Branch name
            }

            // リポジトリ名を抽出 (ツリー表示を見やすくするため)
            // Extract repository name (to make the tree view easier to read).
            string repoName = cloneUrl.TrimEnd('/').Split('/').Last();
            if (repoName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                repoName = repoName.Substring(0, repoName.Length - 4);
            }

            // パストラバーサル対策: repoName が意図したベースディレクトリ外に解決されないことを確認する
            // Path traversal guard: ensure repoName does not resolve outside the intended base directory.
            string baseDir = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ProjectAnalyzer"));
            tempCloneDir = Path.GetFullPath(Path.Combine(baseDir, Guid.NewGuid().ToString(), repoName));
            if (!tempCloneDir.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Invalid repository name in URL: path traversal detected.");
            }
            Directory.CreateDirectory(tempCloneDir);

            // PATによる認証の組み込み
            // Incorporate authentication using PAT.
            string authCloneUrl = cloneUrl;
            string? pat = Environment.GetEnvironmentVariable("GITHUB_PAT");
            if (!string.IsNullOrEmpty(pat))
            {
                var uriBuilder = new UriBuilder(cloneUrl) { UserName = pat };
                authCloneUrl = uriBuilder.ToString();
                Console.WriteLine("🔒 Using Personal Access Token for authentication.");
            }

            // ログ出力の切り替え
            // Switch log output.
            if (string.IsNullOrEmpty(branchName))
            {
                Console.WriteLine($"📥 Cloning repository from {cloneUrl}...");
            }
            else
            {
                Console.WriteLine($"📥 Cloning repository from {cloneUrl} (Branch: {branchName})...");
            }

            // 引数インジェクション対策: ArgumentList を使い各引数を個別要素として渡す
            // Argument injection guard: use ArgumentList to pass each argument as a discrete element.
            var processInfo = new ProcessStartInfo("git")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            processInfo.ArgumentList.Add("clone");
            processInfo.ArgumentList.Add("--depth");
            processInfo.ArgumentList.Add("1");
            if (!string.IsNullOrEmpty(branchName))
            {
                // ブランチ指定がある場合は -b オプションを追加
                // Add -b option if a branch is specified.
                processInfo.ArgumentList.Add("-b");
                processInfo.ArgumentList.Add(branchName);
            }
            processInfo.ArgumentList.Add(authCloneUrl);
            processInfo.ArgumentList.Add(tempCloneDir);

            using var process = Process.Start(processInfo);
            process?.WaitForExit();

            if (process == null || process.ExitCode != 0)
            {
                string error = process?.StandardError.ReadToEnd() ?? "Unknown error";
                // ログにPATが漏れないように authCloneUrl ではなく cloneUrl を使用
                // Use cloneUrl instead of authCloneUrl to prevent PAT leakage in logs.
                string safeCommand = $"git clone --depth 1{(string.IsNullOrEmpty(branchName) ? "" : $" -b {branchName}")} {cloneUrl} {tempCloneDir}";
                throw new Exception($"Git clone failed: {error}\nCommand Executed: {safeCommand}");
            }

            // 分析対象のパスをクローンした一時フォルダに差し替える
            // Replace the analysis target path with the cloned temporary folder.
            targetPath = tempCloneDir;
        }

        // --- 以下は既存のロジック ---
        // --- The following is the existing logic ---
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

        return new AnalyzerSettings(targetPath, outputPath, ignoreList, outputToFile, omitCodeBlockTicks, outputPerFile, tempCloneDir, sanitizeHtmlTags, removeIndent, enableOcr);
    }
}