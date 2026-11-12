using System.Text;

namespace OcrBenchmark;

/// <summary>
/// OCR結果の評価指標です。全角/半角の揺れはNFKCで吸収し、空白・改行は比較から除外します
/// （エンジンごとの空白の入れ方や行の分け方の違いで不利にならないようにするため）。
/// Evaluation metrics for OCR output. Width variants are folded with NFKC, and whitespace and line breaks
/// are excluded so that engines are not penalized for how they insert spaces or split lines.
/// </summary>
internal static class Metrics
{
    public static string Normalize(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in s.Normalize(NormalizationForm.FormKC))
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 文字誤り率（レーベンシュタイン距離 / 正解文字数）。読み順の違いも誤りとして数えます。
    /// Character error rate (Levenshtein distance / ground-truth length). Reading-order differences count as errors.
    /// </summary>
    public static (double cer, int edits, int len) Cer(string predicted, string truth)
    {
        string p = Normalize(predicted), t = Normalize(truth);
        int d = Levenshtein(p, t);
        return ((double)d / Math.Max(1, t.Length), d, t.Length);
    }

    /// <summary>
    /// 読み順を無視した文字の多重集合F1。表や画面のように読み順が一意でない画像の補助指標です。
    /// Order-insensitive character multiset F1, a supplementary metric for images without a unique reading order.
    /// </summary>
    public static double BagF1(string predicted, string truth)
    {
        var p = Normalize(predicted).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        var t = Normalize(truth).GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        int match = p.Sum(kv => Math.Min(kv.Value, t.GetValueOrDefault(kv.Key)));
        int pn = p.Values.Sum(), tn = t.Values.Sum();
        if (pn == 0 || tn == 0) return 0;
        double precision = (double)match / pn, recall = (double)match / tn;
        return precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
