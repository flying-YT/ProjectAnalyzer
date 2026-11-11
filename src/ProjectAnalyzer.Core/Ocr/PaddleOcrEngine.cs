using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using RapidOcrNet;
using SkiaSharp;

namespace ProjectAnalyzer.Core.Ocr;

/// <summary>
/// PP-OCRv6 small (ONNX) を RapidOcrNet 経由で実行するOCRエンジンです。
/// モデルの読み込みは重いため、1インスタンスだけを遅延生成してプロセス内で共有します。
/// ONNX Runtime の推論はスレッドセーフなため、ファイル単位の並列処理から同時に呼び出せます。
/// An OCR engine that runs PP-OCRv6 small (ONNX) through RapidOcrNet.
/// Loading the models is expensive, so a single instance is created lazily and shared within the
/// process. ONNX Runtime inference is thread-safe, so it can be called concurrently from the per-file
/// parallel processing.
/// </summary>
internal static class PaddleOcrEngine
{
    /// <summary>
    /// 出力ディレクトリ内でモデルを配置するフォルダ名です。
    /// The folder name that holds the models in the output directory.
    /// </summary>
    internal const string ModelsDirectoryName = "ocr-models";

    private const string DetectorModelFileName = "PP-OCRv6_det_small.onnx";
    private const string ClassifierModelFileName = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    private const string RecognizerModelFileName = "PP-OCRv6_rec_small.onnx";
    private const string DictionaryFileName = "ppocrv6_dict.txt";

    /// <summary>
    /// 検出前に画像の短辺を拡大する上限（ピクセル）です。
    /// PP-OCRv6 の推奨値は736ですが、小さな画像を大きく拡大するため処理時間とメモリが増えます。
    /// 評価（tools/OcrBenchmark）では320でも精度はほぼ同等で、並列実行時の速度は約2倍、メモリは約3分の1になったため320とします。
    /// The upper limit (in pixels) to which the short side of an image is upscaled before detection.
    /// PP-OCRv6 recommends 736, but that enlarges small images heavily and costs time and memory.
    /// In the evaluation (tools/OcrBenchmark), 320 gave nearly the same accuracy with about 2x the speed and
    /// about a third of the memory under parallel execution, so 320 is used.
    /// </summary>
    internal const int LimitSideLen = 320;

    /// <summary>
    /// 本ツールで使用する前処理オプションです。
    /// PP-OCRv6 は短辺基準のリサイズ・余白なしの前処理で学習されているため、専用プリセットを基にします。
    /// RapidOcrOptions.Default（長辺1024上限・白枠50px）はv5向けで、v6では小さな文字の取りこぼしが増えます。
    /// The preprocessing options used by this tool. PP-OCRv6 was exported for short-side adaptive resizing
    /// without padding, so they are based on its preset. RapidOcrOptions.Default (1024 long-side cap, 50px
    /// border) is tuned for v5 and misses small text on v6.
    /// </summary>
    internal static readonly RapidOcrOptions Options = RapidOcrOptions.PPOCRv6 with { LimitSideLen = LimitSideLen };

    private static readonly Lazy<RapidOcr> Engine = new(CreateEngine);

    /// <summary>
    /// 日本語（非ASCII文字）に隣接する空白を除去するための正規表現です（改行は維持）。
    /// A regex that removes whitespace adjacent to Japanese (non-ASCII) characters, keeping line breaks.
    /// </summary>
    private static readonly Regex SpaceAroundNonAscii =
        new(@"(?<=[^\x00-\x7F])[ \t　]+|[ \t　]+(?=[^\x00-\x7F])", RegexOptions.Compiled);

    /// <summary>
    /// 画像のバイト列からテキストを抽出します。
    /// Extracts text from the bytes of an image.
    /// </summary>
    /// <param name="imageBytes">画像のバイト列 / The image bytes.</param>
    /// <returns>抽出したテキスト。行ごとに改行で区切られます。/ The extracted text, one line per detected text line.</returns>
    /// <exception cref="NotSupportedException">画像形式をデコードできない場合 / When the image format cannot be decoded.</exception>
    internal static string Recognize(byte[] imageBytes)
    {
        // SKBitmap.Decode は未対応形式（EMF/WMF など）で例外を投げるため、先にコーデックの有無で判定する
        // SKBitmap.Decode throws on unsupported formats (EMF/WMF, etc.), so check for a codec first.
        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data)
            ?? throw new NotSupportedException("Unsupported image format.");
        using var bitmap = SKBitmap.Decode(codec)
            ?? throw new NotSupportedException("Unsupported image format.");
        return Recognize(bitmap);
    }

    /// <summary>
    /// デコード済みの画像からテキストを抽出します。前処理オプションは評価ツールでの比較用に差し替えられます。
    /// Extracts text from a decoded image. The preprocessing options can be overridden for comparisons in
    /// the evaluation tool.
    /// </summary>
    /// <param name="bitmap">画像 / The image.</param>
    /// <param name="options">前処理オプション。null の場合は <see cref="Options"/> / Preprocessing options; <see cref="Options"/> when null.</param>
    /// <returns>抽出したテキスト / The extracted text.</returns>
    internal static string Recognize(SKBitmap bitmap, RapidOcrOptions? options = null)
    {
        var result = Engine.Value.Detect(bitmap, options ?? Options);

        var text = string.Join("\n", result.TextBlocks.Select(b => b.Text.Trim()).Where(t => t.Length > 0));
        return SpaceAroundNonAscii.Replace(text, string.Empty);
    }

    /// <summary>
    /// モデルを事前に読み込みます。初回のOCR呼び出し時の待ち時間を避けたい場合に使用します。
    /// Loads the models in advance, to avoid the delay on the first OCR call.
    /// </summary>
    internal static void Warmup() => _ = Engine.Value;

    private static RapidOcr CreateEngine()
    {
        // Lazy で包むことで、並列に初回呼び出しが来てもモデルの読み込みは1回だけになる。
        // 読み込みに失敗した場合は例外がキャッシュされるが、モデル欠落は実行中に直らないため許容する。
        // Wrapping in Lazy ensures the models load only once even under concurrent first calls.
        // A load failure is cached, which is acceptable because missing models do not appear mid-run.
        string dir = Path.Combine(AppContext.BaseDirectory, ModelsDirectoryName);
        var models = RapidOcrModelSet.PPOCRv6Small with
        {
            DetModelPath = Path.Combine(dir, DetectorModelFileName),
            ClsModelPath = Path.Combine(dir, ClassifierModelFileName),
            RecModelPath = Path.Combine(dir, RecognizerModelFileName),
            KeysPath = Path.Combine(dir, DictionaryFileName),
        };

        var files = new[] { models.DetModelPath, models.ClsModelPath, models.RecModelPath, models.KeysPath };
        var missing = files.Where(f => !File.Exists(f)).ToArray();
        if (missing.Length > 0)
        {
            throw new FileNotFoundException(
                $"OCR model files are missing: {string.Join(", ", missing.Select(Path.GetFileName))} (expected in {dir})");
        }

        // 並列化はファイル単位に一本化するため、ONNX Runtime 内部のスレッドは1本に固定する。
        // 内部でも全コアを使うと、ファイル単位並列と二重になりオーバーサブスクリプションで低速化する。
        // 環境変数ではなくセッション単位で設定するため、同一プロセス内の他ライブラリには影響しない。
        // Pin ONNX Runtime to a single internal thread so that parallelism stays at the file level.
        // Using all cores internally as well would oversubscribe the CPU together with the per-file
        // parallelism. This is set per session rather than via environment variables, so other libraries
        // in the same process are not affected.
        var engine = new RapidOcr();
        engine.InitModels(models, numThread: 1);
        return engine;
    }
}
