using System;
using System.IO;

namespace ProjectAnalyzer.Core.Utils;

/// <summary>
/// ファイルやディレクトリがシンボリックリンク（Windowsのジャンクションを含む）かどうかを判定するクラスです。
/// リンクを辿ると解析対象のフォルダ外にあるファイルまで読み取ってしまい、ループするリンクでは走査が
/// 終わらなくなるため、走査処理はリンクを辿らずスキップする判断にこれを使用します。
/// A class that detects whether a file or directory is a symbolic link (including a Windows junction).
/// Following links would read files outside the analyzed folder, and a link that loops would make the
/// traversal never end, so the traversal uses this to skip links instead of following them.
/// </summary>
public static class SymbolicLinkDetector
{
    /// <summary>
    /// 指定されたパスがシンボリックリンクかどうかを判定します。
    /// ファイルとディレクトリのどちらでも判定できます。
    /// Determines whether the specified path is a symbolic link.
    /// It works for both files and directories.
    /// </summary>
    /// <param name="path">判定対象のパス / The path to inspect.</param>
    /// <returns>シンボリックリンクの場合は true / true when the path is a symbolic link.</returns>
    public static bool IsSymbolicLink(string path) => IsSymbolicLink(new FileInfo(path));

    /// <summary>
    /// 指定されたエントリがシンボリックリンクかどうかを判定します。
    /// OneDriveのプレースホルダなどリンク以外の再解析ポイントを除外するため、属性ではなくリンク先の有無で判定します。
    /// Determines whether the specified entry is a symbolic link.
    /// It checks for a link target rather than the attribute, so that reparse points which are not
    /// links (such as OneDrive placeholders) are not mistaken for one.
    /// </summary>
    /// <param name="entry">判定対象のエントリ / The entry to inspect.</param>
    /// <returns>シンボリックリンクの場合は true / true when the entry is a symbolic link.</returns>
    public static bool IsSymbolicLink(FileSystemInfo entry)
    {
        try
        {
            return entry.LinkTarget != null;
        }
        catch (IOException)
        {
            // 判定できないエントリは、安全側に倒してリンクとして扱う（辿らない）
            // An entry that cannot be inspected is treated as a link, erring on the safe side.
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }
}
