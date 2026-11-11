using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OcrBenchmark;
using ProjectAnalyzer.Core.Ocr;

// 使い方 / Usage:
//   generate <outDir> [--per-category N] [--seed S]
//   run <dataDir> --engine ppocr|tesseract --out <result.csv> [--tessdata <dir>] [--parallel N]
//   report <result.csv>... --out <summary.md>
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string Opt(string name, string fallback)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}

string repoRoot = FindRepoRoot();

switch (args.FirstOrDefault())
{
    case "generate":
        new DatasetGenerator(int.Parse(Opt("--seed", "20261112")), repoRoot)
            .Generate(args[1], int.Parse(Opt("--per-category", "30")));
        Console.WriteLine($"Generated dataset in {args[1]}");
        break;
    case "run":
        Run(args[1], Opt("--engine", "small"), Opt("--out", "result.csv"), Opt("--tessdata", ""), int.Parse(Opt("--parallel", "1")));
        break;
    case "report":
        Report(args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList(), Opt("--out", "summary.md"));
        break;
    default:
        Console.WriteLine("usage: generate <outDir> | run <dataDir> --engine ppocr|tesseract --out <csv> | report <csv>... --out <md>");
        break;
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ProjectAnalyzer.sln"))) dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}

static void Run(string dataDir, string engine, string outCsv, string tessdata, int parallel)
{
    var images = Directory.EnumerateFiles(dataDir, "*.*", SearchOption.AllDirectories)
        .Where(f => f.EndsWith(".png") || f.EndsWith(".jpg"))
        .Where(f => File.Exists(Path.ChangeExtension(f, ".txt")))
        .OrderBy(f => f, StringComparer.Ordinal).ToList();

    Func<string, string> ocr;
    double loadMs = 0;
    if (engine == "tesseract")
    {
        ocr = path => Tesseract(path, tessdata);
    }
    else
    {
        if (engine != "ppocr") throw new ArgumentException($"Unknown engine '{engine}'. Use 'ppocr' or 'tesseract'.");
        var sw = Stopwatch.StartNew();
        PaddleOcrEngine.Warmup();
        loadMs = sw.Elapsed.TotalMilliseconds;

        // 前処理（短辺の拡大上限）の比較用。未指定なら本体と同じ設定を使う。
        // For comparing preprocessing (short-side upscale limit). Unset means the same options as the product.
        var limit = Environment.GetEnvironmentVariable("OCRBENCH_LIMIT_SIDE_LEN");
        var options = limit == null ? null : PaddleOcrEngine.Options with { LimitSideLen = int.Parse(limit) };
        ocr = path =>
        {
            using var bmp = SkiaSharp.SKBitmap.Decode(path);
            return PaddleOcrEngine.Recognize(bmp, options);
        };
    }

    var predDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outCsv))!, "predictions", engine);
    Directory.CreateDirectory(predDir);
    var rows = new string[images.Count];
    var total = Stopwatch.StartNew();
    Parallel.For(0, images.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, i =>
    {
        string img = images[i];
        string truth = File.ReadAllText(Path.ChangeExtension(img, ".txt"));
        var sw = Stopwatch.StartNew();
        string pred;
        try { pred = ocr(img); } catch (Exception ex) { pred = ""; Console.Error.WriteLine($"{img}: {ex.Message}"); }
        double ms = sw.Elapsed.TotalMilliseconds;
        File.WriteAllText(Path.Combine(predDir, Path.GetFileNameWithoutExtension(img) + ".txt"), pred);

        var (cer, edits, len) = Metrics.Cer(pred, truth);
        double f1 = Metrics.BagF1(pred, truth);
        string category = Path.GetFileName(Path.GetDirectoryName(img)!);
        rows[i] = $"{engine},{category},{Path.GetFileName(img)},{cer:F4},{edits},{len},{f1:F4},{ms:F1}";
    });
    double wallMs = total.Elapsed.TotalMilliseconds;

    File.WriteAllLines(outCsv, new[] { "engine,category,file,cer,edits,len,f1,ms" }.Concat(rows));
    long peakMb = Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024;
    File.WriteAllText(outCsv + ".meta",
        $"engine={engine}\nimages={images.Count}\nparallel={parallel}\nload_ms={loadMs:F0}\nwall_ms={wallMs:F0}\npeak_working_set_mb={peakMb}\n");
    Console.WriteLine($"{engine}: {images.Count} images, wall {wallMs / 1000:F1}s (parallel {parallel}), load {loadMs:F0}ms, peak {peakMb}MB");
}

static string Tesseract(string path, string tessdata)
{
    // 現行実装（Linuxのフォールバック）と同じ jpn+eng・OMP_THREAD_LIMIT=1・空白除去で実行する。
    // Run with the same jpn+eng, OMP_THREAD_LIMIT=1 and whitespace cleanup as the current implementation.
    var psi = new ProcessStartInfo("tesseract")
    {
        RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        StandardOutputEncoding = Encoding.UTF8,
    };
    psi.ArgumentList.Add(path);
    psi.ArgumentList.Add("stdout");
    psi.ArgumentList.Add("-l");
    psi.ArgumentList.Add("jpn+eng");
    if (!string.IsNullOrEmpty(tessdata))
    {
        psi.ArgumentList.Add("--tessdata-dir");
        psi.ArgumentList.Add(tessdata);
    }
    psi.EnvironmentVariables["OMP_THREAD_LIMIT"] = "1";
    using var p = Process.Start(psi)!;
    string text = p.StandardOutput.ReadToEnd();
    p.StandardError.ReadToEnd();
    p.WaitForExit();
    return Regex.Replace(text, @"(?<=[^\x00-\x7F])[ \t　]+|[ \t　]+(?=[^\x00-\x7F])", "").Trim();
}

static void Report(List<string> csvs, string outMd)
{
    var rows = csvs.SelectMany(c => File.ReadLines(c).Skip(1)).Select(l => l.Split(','))
        .Select(c => (engine: c[0], cat: c[1], edits: int.Parse(c[4]), len: int.Parse(c[5]), f1: double.Parse(c[6]), ms: double.Parse(c[7])))
        .ToList();
    var engines = rows.Select(r => r.engine).Distinct().ToList();
    var cats = rows.Select(r => r.cat).Distinct().OrderBy(c => Array.IndexOf(DatasetGenerator.Categories, c)).ToList();
    var sb = new StringBuilder();

    string Cer(IEnumerable<(string engine, string cat, int edits, int len, double f1, double ms)> rs)
    {
        var l = rs.ToList();
        return $"{100.0 * l.Sum(r => r.edits) / Math.Max(1, l.Sum(r => r.len)):F1}%";
    }

    sb.AppendLine("## CER（文字誤り率、低いほど良い） / Character error rate (lower is better)");
    sb.AppendLine();
    sb.AppendLine($"| カテゴリ | 枚数 | {string.Join(" | ", engines)} |");
    sb.AppendLine($"|---|---|{string.Concat(engines.Select(_ => "---|"))}");
    foreach (var cat in cats)
    {
        int n = rows.Count(r => r.cat == cat && r.engine == engines[0]);
        sb.AppendLine($"| {cat} | {n} | {string.Join(" | ", engines.Select(e => Cer(rows.Where(r => r.engine == e && r.cat == cat))))} |");
    }
    sb.AppendLine($"| **全体（縦書き除く）** | {rows.Count(r => r.engine == engines[0] && r.cat != "vertical_jp")} | {string.Join(" | ", engines.Select(e => "**" + Cer(rows.Where(r => r.engine == e && r.cat != "vertical_jp")) + "**"))} |");
    sb.AppendLine();

    sb.AppendLine("## 文字F1（読み順を無視、高いほど良い） / Order-insensitive character F1 (higher is better)");
    sb.AppendLine();
    sb.AppendLine($"| カテゴリ | {string.Join(" | ", engines)} |");
    sb.AppendLine($"|---|{string.Concat(engines.Select(_ => "---|"))}");
    foreach (var cat in cats)
    {
        sb.AppendLine($"| {cat} | {string.Join(" | ", engines.Select(e => $"{100 * rows.Where(r => r.engine == e && r.cat == cat).Average(r => r.f1):F1}"))} |");
    }
    sb.AppendLine();

    sb.AppendLine("## 速度・メモリ / Speed and memory");
    sb.AppendLine();
    sb.AppendLine("| エンジン | 1枚あたり中央値 | 1枚あたり平均 | モデル読込 | 全体所要（並列度） | ピークメモリ |");
    sb.AppendLine("|---|---|---|---|---|---|");
    foreach (var (e, csv) in engines.Zip(csvs))
    {
        var ms = rows.Where(r => r.engine == e).Select(r => r.ms).OrderBy(x => x).ToList();
        var meta = File.Exists(csv + ".meta")
            ? File.ReadAllLines(csv + ".meta").Select(l => l.Split('=')).ToDictionary(p => p[0], p => p[1])
            : new Dictionary<string, string>();
        sb.AppendLine($"| {e} | {ms[ms.Count / 2]:F0} ms | {ms.Average():F0} ms | {meta.GetValueOrDefault("load_ms", "-")} ms | {double.Parse(meta.GetValueOrDefault("wall_ms", "0")) / 1000:F1} s ({meta.GetValueOrDefault("parallel", "-")}) | {meta.GetValueOrDefault("peak_working_set_mb", "-")} MB |");
    }
    File.WriteAllText(outMd, sb.ToString());
    Console.WriteLine(sb.ToString());
}
