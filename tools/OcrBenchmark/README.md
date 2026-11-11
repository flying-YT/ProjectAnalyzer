# OcrBenchmark

OCRエンジン（本体の PP-OCRv6 small と、比較用の Tesseract）の精度と速度を比較する開発用ツールです。
ソリューションには含めておらず、NuGetパッケージにも含まれません。
v1.4.0 でのOCRエンジン移行（Tesseract → PP-OCRv6 small）と、前処理の設定（短辺の拡大上限 320）はこのツールの評価結果に基づいて決めています。

## 使い方

```bash
dotnet build tools/OcrBenchmark -c Release
B="dotnet tools/OcrBenchmark/bin/Release/net9.0/OcrBenchmark.dll"

# 1. 評価用画像（正解テキスト付き）を生成する。Noto Sans/Serif CJK JP フォントが必要。
$B generate ./ocr-dataset --per-category 30

# 2. エンジンごとに実行する（--parallel 1 で1枚あたりの処理時間を、--parallel 8 で本番に近い処理量とメモリを測る）
$B run ./ocr-dataset --engine ppocr --out ./results/ppocr.csv
# Tesseract は tesseract コマンド（jpn+eng）を使う。--tessdata で学習データの場所を指定できる。
$B run ./ocr-dataset --engine tesseract --tessdata ./tessdata --out ./results/tesseract.csv

# 3. 集計する
$B report ./results/tesseract.csv ./results/ppocr.csv --out ./results/summary.md
```

認識結果は `results/predictions/<engine>/` に画像ごとのテキストで保存されます。

前処理（検出前に画像の短辺を拡大する上限）を変えて比較する場合は、環境変数 `OCRBENCH_LIMIT_SIDE_LEN` を指定します（未指定時は本体と同じ 320）。

```bash
OCRBENCH_LIMIT_SIDE_LEN=736 $B run ./ocr-dataset --engine ppocr --out ./results/ppocr-736.csv
```

## 実データで評価する

`run` は、指定フォルダ以下の `*.png` / `*.jpg` のうち、同名の `*.txt`（正解テキスト、UTF-8）があるものを評価します。
サブフォルダ名がカテゴリ名として集計されます。実際のOffice文書から取り出した画像に正解テキストを付けて置けば、
合成データと同じ手順で評価できます。

```
my-dataset/
  screenshots/
    001.png
    001.txt
  scans/
    001.jpg
    001.txt
```

## 指標

- **CER（文字誤り率）**: レーベンシュタイン距離 ÷ 正解文字数。NFKC正規化し、空白・改行は除外して比較します。読み順の違いも誤りとして数えます。
- **文字F1**: 読み順を無視した文字の多重集合のF1。表や画面のように読み順が一意に決まらない画像の補助指標です。
