using System.Text;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OcrBenchmark;

/// <summary>
/// 正解テキスト付きの評価用画像を合成します。Office文書に貼られがちな画像（文書の抜粋、スクリーンショット、
/// 表、スライド、スキャン）を想定し、カテゴリごとに描画条件を変えます。
/// Synthesizes evaluation images with ground-truth text, modeled on images commonly pasted into Office
/// documents (document excerpts, screenshots, tables, slides, scans), with per-category rendering.
/// </summary>
internal sealed class DatasetGenerator
{
    private readonly Random _rng;
    private readonly List<string> _jpLines;
    private readonly List<string> _enLines;
    private readonly SKTypeface _sans;
    private readonly SKTypeface _serif;
    private readonly SKTypeface _mono;

    public static readonly string[] Categories =
    {
        "doc_jp", "small_jp", "ui_screenshot", "table", "slide", "scan_degraded", "english_code", "low_contrast", "vertical_jp",
    };

    public DatasetGenerator(int seed, string repoRoot)
    {
        _rng = new Random(seed);
        _jpLines = Corpus.LoadJapanese(Path.Combine(repoRoot, "README.md"));
        _enLines = Corpus.LoadEnglish(Path.Combine(repoRoot, "README_EN.md"));
        _sans = RequireFont("Noto Sans CJK JP");
        _serif = RequireFont("Noto Serif CJK JP");
        _mono = RequireFont("Noto Sans Mono CJK JP");
    }

    private static SKTypeface RequireFont(string family)
    {
        var tf = SKTypeface.FromFamilyName(family);
        if (tf == null || !tf.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Font '{family}' is not installed (got '{tf?.FamilyName}').");
        }
        return tf;
    }

    public void Generate(string outDir, int perCategory)
    {
        foreach (var category in Categories)
        {
            string dir = Path.Combine(outDir, category);
            Directory.CreateDirectory(dir);
            for (int i = 0; i < perCategory; i++)
            {
                var (bitmap, truth, jpeg) = Render(category);
                string name = $"{category}_{i:D3}";
                using (bitmap)
                using (var data = bitmap.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, jpeg ? 45 : 100))
                using (var fs = File.Create(Path.Combine(dir, name + (jpeg ? ".jpg" : ".png"))))
                {
                    data.SaveTo(fs);
                }
                File.WriteAllText(Path.Combine(dir, name + ".txt"), truth, new UTF8Encoding(false));
            }
        }
    }

    private (SKBitmap, string, bool) Render(string category) => category switch
    {
        "doc_jp" => (Paragraph(Pick(_sans, _serif), _rng.Next(16, 29), SKColors.White, SKColors.Black, Lines(_jpLines, 2, 6)), Last, false),
        "small_jp" => (Paragraph(_sans, _rng.Next(11, 14), SKColors.White, new SKColor(0x33, 0x33, 0x33), Lines(_jpLines, 3, 8)), Last, false),
        "low_contrast" => (Paragraph(_sans, _rng.Next(14, 22), new SKColor(0xEE, 0xEE, 0xEE), new SKColor(0x9A, 0x9A, 0x9A), Lines(_jpLines, 2, 5)), Last, false),
        "english_code" => (Paragraph(Pick(_mono, _sans), _rng.Next(13, 21), SKColors.White, SKColors.Black, Lines(_enLines, 2, 6)), Last, false),
        "scan_degraded" => (Degrade(Paragraph(Pick(_sans, _serif), _rng.Next(16, 25), SKColors.White, SKColors.Black, Lines(_jpLines, 2, 6))), Last, true),
        "ui_screenshot" => (Screenshot(), Last, false),
        "table" => (Table(), Last, false),
        "slide" => (Slide(), Last, false),
        "vertical_jp" => (Vertical(), Last, false),
        _ => throw new ArgumentException(category),
    };

    /// <summary>直近に描画した画像の正解テキスト / The ground truth of the most recently rendered image.</summary>
    private string Last = "";

    private SKTypeface Pick(params SKTypeface[] faces) => faces[_rng.Next(faces.Length)];

    private List<string> Lines(List<string> source, int min, int max)
    {
        int n = _rng.Next(min, max + 1);
        int start = _rng.Next(source.Count - n);
        return source.GetRange(start, n);
    }

    private static SKFont Font(SKTypeface tf, float size) => new(tf, size) { Edging = SKFontEdging.Antialias, Subpixel = true };

    /// <summary>
    /// 指定幅で折り返したときの行一覧を返します。正解テキストも同じ折り返し結果を使います。
    /// Returns the lines wrapped at the given width. The ground truth uses the same wrapped lines.
    /// </summary>
    private static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in text)
        {
            current.Append(ch);
            if (font.MeasureText(current.ToString()) > maxWidth && current.Length > 1)
            {
                current.Length--;
                result.Add(current.ToString().TrimEnd());
                current.Clear().Append(ch);
            }
        }
        if (current.ToString().Trim().Length > 0) result.Add(current.ToString().Trim());
        return result;
    }

    private SKBitmap Paragraph(SKTypeface tf, float size, SKColor bg, SKColor fg, List<string> lines)
    {
        using var font = Font(tf, size);
        float width = _rng.Next(480, 960);
        var wrapped = lines.SelectMany(l => Wrap(l, font, width - 2 * size)).ToList();
        float lineHeight = size * 1.6f;
        int height = (int)(wrapped.Count * lineHeight + 2 * size);

        var bmp = new SKBitmap((int)width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(bg);
        using var paint = new SKPaint { Color = fg, IsAntialias = true };
        for (int i = 0; i < wrapped.Count; i++)
        {
            canvas.DrawText(wrapped[i], size, size + (i + 0.8f) * lineHeight, font, paint);
        }
        Last = string.Join("\n", wrapped);
        return bmp;
    }

    private SKBitmap Degrade(SKBitmap src)
    {
        // わずかな回転・ぼかし・ノイズでスキャン/撮影を模す（JPEG圧縮は保存時に行う）
        // Mimic a scan/photo with slight rotation, blur and noise (JPEG compression happens on save).
        var dst = new SKBitmap(src.Width, src.Height);
        using (var canvas = new SKCanvas(dst))
        using (var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(0.7f, 0.7f), IsAntialias = true })
        {
            canvas.Clear(new SKColor(0xF4, 0xF1, 0xE8));
            canvas.RotateDegrees((float)(_rng.NextDouble() * 3 - 1.5), src.Width / 2f, src.Height / 2f);
            canvas.DrawBitmap(src, 0, 0, paint);
        }
        src.Dispose();
        for (int y = 0; y < dst.Height; y++)
        for (int x = 0; x < dst.Width; x++)
        {
            var c = dst.GetPixel(x, y);
            int n = _rng.Next(-18, 19);
            dst.SetPixel(x, y, new SKColor(Clamp(c.Red + n), Clamp(c.Green + n), Clamp(c.Blue + n)));
        }
        return dst;
    }

    private static byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);

    private SKBitmap Screenshot()
    {
        // ウィンドウ風の画面：タイトルバー、ラベルと入力値、ボタン、ステータス行
        // A window-like screen: title bar, label/value pairs, buttons and a status line.
        int width = 900, rowH = 38;
        var labels = new[] { "プロジェクト名", "出力先フォルダ", "最大サイズ (MB)", "除外パターン", "作成日", "担当者", "ステータス", "API キー" };
        var values = new[] { "ProjectAnalyzer", "C:\\Users\\dev\\output", "4", "*.log; bin/; obj/", "2026/11/12", "山田 太郎", "処理中 (42%)", "sk-test-1234" };
        var buttons = new[] { "実行", "キャンセル", "設定を保存", "参照...", "OK", "ヘルプ" };
        int rows = _rng.Next(3, 7);
        var order = Enumerable.Range(0, labels.Length).OrderBy(_ => _rng.Next()).Take(rows).ToArray();
        int height = 60 + rows * rowH + 90;

        var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        var theme = _rng.Next(2) == 0
            ? (bg: new SKColor(0xF3, 0xF3, 0xF3), bar: new SKColor(0x2B, 0x57, 0x9A), fg: new SKColor(0x20, 0x20, 0x20), field: SKColors.White)
            : (bg: new SKColor(0x1E, 0x1E, 0x1E), bar: new SKColor(0x3C, 0x3C, 0x3C), fg: new SKColor(0xDD, 0xDD, 0xDD), field: new SKColor(0x2D, 0x2D, 0x2D));
        canvas.Clear(theme.bg);
        using var font = Font(_sans, 15);
        using var titleFont = Font(_sans, 16);
        using var fg = new SKPaint { Color = theme.fg, IsAntialias = true };
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var fill = new SKPaint { Color = theme.bar };
        using var field = new SKPaint { Color = theme.field };
        using var border = new SKPaint { Color = new SKColor(0x88, 0x88, 0x88), IsStroke = true };

        var truth = new List<string>();
        string title = "設定 - Project Analyzer";
        canvas.DrawRect(0, 0, width, 36, fill);
        canvas.DrawText(title, 14, 24, titleFont, white);
        truth.Add(title);

        for (int r = 0; r < rows; r++)
        {
            float y = 60 + r * rowH;
            canvas.DrawText(labels[order[r]], 24, y + 20, font, fg);
            canvas.DrawRect(220, y, 520, 28, field);
            canvas.DrawRect(220, y, 520, 28, border);
            canvas.DrawText(values[order[r]], 230, y + 20, font, fg);
            truth.Add(labels[order[r]] + " " + values[order[r]]);
        }

        float by = 60 + rows * rowH + 20;
        var chosen = buttons.OrderBy(_ => _rng.Next()).Take(3).ToArray();
        float bx = 220;
        foreach (var b in chosen)
        {
            float w = font.MeasureText(b) + 40;
            canvas.DrawRoundRect(bx, by, w, 32, 4, 4, fill);
            canvas.DrawText(b, bx + 20, by + 22, font, white);
            bx += w + 16;
        }
        truth.Add(string.Join(" ", chosen));
        Last = string.Join("\n", truth);
        return bmp;
    }

    private SKBitmap Table()
    {
        var headers = new[] { "No.", "項目", "担当", "期限", "状態", "備考" };
        var items = new[] { "要件定義", "基本設計", "詳細設計", "結合テスト", "性能評価", "リリース判定", "移行手順書", "OCR精度比較" };
        var people = new[] { "佐藤", "鈴木", "高橋", "田中", "伊藤", "渡辺" };
        var states = new[] { "完了", "進行中", "未着手", "保留" };
        var notes = new[] { "-", "レビュー待ち", "v1.4.0で対応", "要確認", "顧客承認済み" };
        int cols = _rng.Next(4, 7), rows = _rng.Next(3, 7);
        float[] widths = { 60, 160, 90, 120, 90, 150 };
        float rowH = 34, totalW = widths.Take(cols).Sum() + 20;

        var bmp = new SKBitmap((int)totalW, (int)((rows + 1) * rowH + 20));
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var font = Font(_sans, 15);
        using var text = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        using var head = new SKPaint { Color = new SKColor(0xD9, 0xE2, 0xF3) };
        using var line = new SKPaint { Color = new SKColor(0x70, 0x70, 0x70), IsStroke = true };

        var truth = new List<string>();
        for (int r = 0; r <= rows; r++)
        {
            var cells = new List<string>();
            float x = 10, y = 10 + r * rowH;
            for (int c = 0; c < cols; c++)
            {
                string v = r == 0 ? headers[c] : c switch
                {
                    0 => r.ToString(),
                    1 => items[_rng.Next(items.Length)],
                    2 => people[_rng.Next(people.Length)],
                    3 => $"2026/{_rng.Next(1, 13):D2}/{_rng.Next(1, 29):D2}",
                    4 => states[_rng.Next(states.Length)],
                    _ => notes[_rng.Next(notes.Length)],
                };
                if (r == 0) canvas.DrawRect(x, y, widths[c], rowH, head);
                canvas.DrawRect(x, y, widths[c], rowH, line);
                canvas.DrawText(v, x + 8, y + 23, font, text);
                cells.Add(v);
                x += widths[c];
            }
            truth.Add(string.Join(" ", cells));
        }
        Last = string.Join("\n", truth);
        return bmp;
    }

    private SKBitmap Slide()
    {
        int width = 1280, height = 720;
        var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        var bgColors = new[] { new SKColor(0xFF, 0xFF, 0xFF), new SKColor(0x1F, 0x3A, 0x5F), new SKColor(0xF7, 0xF3, 0xE8) };
        var bg = bgColors[_rng.Next(bgColors.Length)];
        var fgColor = bg.Red < 0x80 ? SKColors.White : new SKColor(0x22, 0x22, 0x22);
        canvas.Clear(bg);
        using var titleFont = Font(_sans, 44);
        using var bodyFont = Font(_sans, 26);
        using var fg = new SKPaint { Color = fgColor, IsAntialias = true };
        using var accent = new SKPaint { Color = new SKColor(0xE0, 0x7A, 0x1F) };

        var truth = new List<string>();
        var titles = new[] { "OCRエンジン移行計画", "PP-OCRv6 評価結果", "今後のロードマップ", "課題と対応方針", "性能比較のまとめ" };
        string title = titles[_rng.Next(titles.Length)];
        canvas.DrawText(title, 80, 120, titleFont, fg);
        canvas.DrawRect(80, 140, 200, 6, accent);
        truth.Add(title);

        float y = 220;
        foreach (var l in Lines(_jpLines, 3, 5))
        {
            foreach (var w in Wrap(l, bodyFont, width - 220).Take(2))
            {
                if (y > height - 40) break;
                canvas.DrawText("・" + w, 100, y, bodyFont, fg);
                truth.Add("・" + w);
                y += 46;
            }
        }
        Last = string.Join("\n", truth);
        return bmp;
    }

    private SKBitmap Vertical()
    {
        // 縦書き：右の列から左へ読む。正解は列ごとに1行とする。
        // Vertical writing: columns read right to left. The ground truth has one line per column.
        float size = _rng.Next(18, 27);
        int perColumn = 16;
        // 縦書きで自然な文字（かな・漢字・句読点）だけを残し、40〜96文字になるまで文を足す
        // Keep only characters natural in vertical writing and append sentences until 40-96 chars.
        var text = "";
        while (text.Length < 40)
        {
            text += Regex.Replace(_jpLines[_rng.Next(_jpLines.Count)], @"[^\u3040-\u30FF\u4E00-\u9FFF、。]", "");
        }
        if (text.Length > perColumn * 6) text = text[..(perColumn * 6)];
        var columns = Enumerable.Range(0, (text.Length + perColumn - 1) / perColumn)
            .Select(i => text.Substring(i * perColumn, Math.Min(perColumn, text.Length - i * perColumn))).ToList();

        int width = (int)(columns.Count * size * 1.8f + size * 2), height = (int)(perColumn * size * 1.15f + size * 2);
        var bmp = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var font = Font(_serif, size);
        using var fg = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        for (int c = 0; c < columns.Count; c++)
        {
            float x = width - size * 1.5f - c * size * 1.8f;
            for (int i = 0; i < columns[c].Length; i++)
            {
                canvas.DrawText(columns[c][i].ToString(), x, size * 1.5f + i * size * 1.15f, font, fg);
            }
        }
        Last = string.Join("\n", columns);
        return bmp;
    }
}

/// <summary>
/// README からOCRの正解に使う文を取り出します（Markdown記法やURLを除去）。
/// Extracts sentences from the READMEs to use as OCR ground truth (Markdown syntax and URLs removed).
/// </summary>
internal static class Corpus
{
    private static string Clean(string line)
    {
        line = Regex.Replace(line, @"!\[[^\]]*\]\([^)]*\)", "");
        line = Regex.Replace(line, @"\[([^\]]*)\]\([^)]*\)", "$1");
        line = Regex.Replace(line, @"https?://\S+", "");
        line = Regex.Replace(line, @"\\(?=[\\_*\[\]()#.\-!`])", "");
        line = Regex.Replace(line, @"[*`#>|]|<[^>]+>", "");
        line = Regex.Replace(line, @"\p{So}|\p{Cs}|\uFE0F", "");
        line = Regex.Replace(line, @"^\s*[-+]\s+", "");
        return Regex.Replace(line, @"\s+", " ").Trim();
    }

    public static List<string> LoadJapanese(string path) => File.ReadAllLines(path)
        .Select(Clean)
        .Where(l => l.Length >= 12 && Regex.IsMatch(l, @"[\u3040-\u30FF\u4E00-\u9FFF]"))
        .Distinct().ToList();

    public static List<string> LoadEnglish(string path) => File.ReadAllLines(path)
        .Select(Clean)
        .Where(l => l.Length >= 12 && !Regex.IsMatch(l, @"[^\x00-\x7F]"))
        .Distinct().ToList();
}
