using System;
using System.IO;
using Xunit;

namespace ProjectAnalyzer.Core.Tests;

/// <summary>
/// シンボリックリンクを作成できる環境かどうかを判定します。
/// Windowsでは管理者権限や開発者モードが必要なため、作成できない環境ではテストをスキップします。
/// Determines whether the environment can create symbolic links.
/// Windows requires administrator rights or developer mode, so the tests are skipped where it cannot.
/// </summary>
internal static class SymbolicLinkSupport
{
    private static readonly Lazy<bool> Available = new Lazy<bool>(Probe);

    /// <summary>
    /// シンボリックリンクを作成できる場合は true / true when symbolic links can be created.
    /// </summary>
    public static bool IsAvailable => Available.Value;

    private static bool Probe()
    {
        string probeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(probeDir);

            string target = Path.Combine(probeDir, "target.txt");
            File.WriteAllText(target, "probe");
            File.CreateSymbolicLink(Path.Combine(probeDir, "link.txt"), target);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (Directory.Exists(probeDir)) Directory.Delete(probeDir, true);
        }
    }
}

/// <summary>
/// シンボリックリンクを作成できる環境でのみ実行されるテストであることを示します。
/// Marks a test that runs only where the environment can create symbolic links.
/// </summary>
public sealed class SymbolicLinkFactAttribute : FactAttribute
{
    public SymbolicLinkFactAttribute()
    {
        if (!SymbolicLinkSupport.IsAvailable)
        {
            Skip = "シンボリックリンクを作成できない環境のためスキップします。 / Skipped: this environment cannot create symbolic links.";
        }
    }
}
