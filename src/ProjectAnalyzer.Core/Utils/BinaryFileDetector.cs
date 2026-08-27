using System;
using System.Collections.Generic;
using System.IO;

namespace ProjectAnalyzer.Core.Utils;

/// <summary>
/// テキストとして読み込めないファイル（PDFや画像などのバイナリ）を判定するクラスです。
/// これらを素通しで読み込むと文字化けした内容が出力へ混ざり、AIへ渡すコンテキストを汚してしまうため、
/// 内容の抽出対象から外す判断に使用します。
/// A class that detects files which cannot be read as text (binaries such as PDFs and images).
/// Reading them as-is would leak mojibake into the output and pollute the context handed to the AI,
/// so this is used to decide which files to exclude from content extraction.
/// </summary>
public static class BinaryFileDetector
{
    /// <summary>
    /// 内容がテキストでないと分かっている拡張子の一覧です。
    /// Office形式(.docx/.docm/.xlsx/.xls/.xlsm/.pptx)は専用の抽出処理があるため、ここには含めません。
    /// The set of extensions whose content is known not to be text.
    /// Office formats have dedicated extraction paths, so they are deliberately excluded from this set.
    /// </summary>
    private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // 文書 / Documents
        ".pdf", ".doc", ".ppt", ".xlsb", ".odt", ".ods", ".odp", ".rtf",

        // 画像 / Images
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".ico",
        ".webp", ".heic", ".heif", ".psd", ".ai", ".eps",

        // 音声・動画 / Audio and video
        ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac",
        ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".webm", ".flv",

        // 書庫 / Archives
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".lzh", ".cab", ".iso", ".dmg",

        // 実行ファイル・ライブラリ / Executables and libraries
        ".exe", ".dll", ".so", ".dylib", ".pdb", ".lib", ".a", ".class", ".pyc", ".msi",

        // フォント / Fonts
        ".woff", ".woff2", ".ttf", ".otf", ".eot",

        // データベース等 / Databases and similar
        ".db", ".sqlite", ".sqlite3", ".mdb", ".accdb", ".dat", ".bin",
    };

    /// <summary>
    /// 内容の抽出対象外とすべきバイナリファイルかどうかを判定します。
    /// 既知のバイナリ拡張子に一致するか、拡張子から判断できない場合は先頭の内容を調べます。
    /// Determines whether a file is a binary that should be excluded from content extraction.
    /// It matches against the known binary extensions, and sniffs the leading bytes when the
    /// extension is inconclusive.
    /// </summary>
    /// <param name="filePath">判定対象のファイルパス / The path of the file to inspect.</param>
    /// <returns>バイナリと判定された場合は true / true when the file is considered binary.</returns>
    public static bool IsBinaryFile(string filePath)
    {
        if (BinaryExtensions.Contains(Path.GetExtension(filePath))) return true;

        try
        {
            return HasBinaryContent(filePath);
        }
        catch (IOException)
        {
            // 読み取れないファイルはバイナリ判定せず、従来どおり読み込み側でエラーを扱わせる
            // An unreadable file is not treated as binary; the reader reports the error as before.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// 先頭の内容を調べてバイナリかどうかを判定します。
    /// BOMを持つファイルはテキストとして扱い、それ以外はNULバイトの有無で判定します。
    /// テキストファイルにNULバイトが現れることは通常ないため、拡張子に依存しない確実な指標になります。
    /// Sniffs the leading bytes to decide whether the content is binary.
    /// A file with a BOM is treated as text; otherwise the presence of a NUL byte decides.
    /// NUL bytes practically never occur in text files, which makes this a reliable,
    /// extension-independent signal.
    /// </summary>
    /// <param name="filePath">判定対象のファイルパス / The path of the file to inspect.</param>
    /// <returns>バイナリと判定された場合は true / true when the content is considered binary.</returns>
    private static bool HasBinaryContent(string filePath)
    {
        const int sampleSize = 8000;

        using var stream = File.OpenRead(filePath);

        var buffer = new byte[sampleSize];
        int read = stream.Read(buffer, 0, buffer.Length);
        if (read == 0) return false;

        // BOM付きのUTF-16/UTF-32はNULバイトを含むが正当なテキストのため、先にBOMを判定する
        // UTF-16/UTF-32 with a BOM contains NUL bytes but is valid text, so check the BOM first.
        if (HasTextBom(buffer, read)) return false;

        for (int i = 0; i < read; i++)
        {
            if (buffer[i] == 0x00) return true;
        }

        return false;
    }

    /// <summary>
    /// 先頭バイト列がテキストのBOM(UTF-8 / UTF-16 / UTF-32)で始まっているかを判定します。
    /// Determines whether the leading bytes start with a text BOM (UTF-8 / UTF-16 / UTF-32).
    /// </summary>
    /// <param name="buffer">判定対象のバイト列 / The bytes to inspect.</param>
    /// <param name="length">有効なバイト数 / The number of valid bytes.</param>
    /// <returns>BOMで始まっている場合は true / true when the bytes start with a BOM.</returns>
    private static bool HasTextBom(byte[] buffer, int length)
    {
        // UTF-8
        if (length >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF) return true;

        // UTF-32 は UTF-16 とBOMの先頭2バイトが同じため、先に4バイトで判定する
        // UTF-32 shares its first two BOM bytes with UTF-16, so check the 4-byte form first.
        if (length >= 4 && buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00) return true;
        if (length >= 4 && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF) return true;

        // UTF-16
        if (length >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE) return true;
        if (length >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF) return true;

        return false;
    }
}
