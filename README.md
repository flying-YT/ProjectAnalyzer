# **Project Analyzer**

[![NuGet Version](https://img.shields.io/nuget/v/ProjectAnalyzer.Core.svg)](https://www.nuget.org/packages/ProjectAnalyzer.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ProjectAnalyzer.Core.svg)](https://www.nuget.org/packages/ProjectAnalyzer.Core)

**日本語** | [English](https://github.com/flying-YT/ProjectAnalyzer/blob/master/README_EN.md)

📥 **[最新版をダウンロード (Releases)](https://github.com/flying-YT/ProjectAnalyzer/releases/latest)** ｜ 📦 **[NuGet パッケージ](https://www.nuget.org/packages/ProjectAnalyzer.Core)**

> [!IMPORTANT]
> **v1.4.0 での主な変更点**
> * **OCRエンジンを変更しました。** Tesseract から、[PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) の **PP-OCRv6 small** モデル（ONNX Runtime で実行）へ移行しました。OS側へのTesseractのインストールや学習データ（tessdata）の配置は不要になり、Windows / Linux / macOS のいずれもNuGetパッケージだけで動作します。日本語の画面キャプチャ・小さい文字・表などの認識精度も大きく向上しています。
> * **対応する .NET のバージョンを .NET 8 以降に変更しました。** .NET 6 / .NET 7 はサポート対象外となりました（いずれもマイクロソフトのサポートが終了しています）。引き続き .NET 6 / 7 で利用する場合は v1.3.2 以前をご利用ください。

## **概要**

**Project Analyzer** は、指定されたプロジェクトフォルダ**またはGitHubリポジトリ**の構造と内容を分析し、AI (LLM) のコンテキストとして利用しやすいように、Markdownファイルとして出力する.NET製のコマンドラインツールおよびクラスライブラリです。

主に**Githubのリポジトリ**や**自作のプロジェクトフォルダ**を**NotebookLM**などのAIツールのソースとして使う際に活用できます。

さらに、ソースコードだけでなく**WordやExcel、PowerPointなどのOfficeソフトのファイル読み込みにも対応**しており、仕様書や設計データも含めたプロジェクト全体のコンテキストをAIに提供することが可能です。

## **活用例**

このライブラリを使ったWebアプリケーションのソースコードを公開しています。実際の組み込み例として参考にできます。

**[ProjectAnalyzer Web Service](https://github.com/flying-YT/ProjectAnalyzerWebService)**

## **主な機能**

* **🐙 GitHubリポジトリの直接分析** GitHubのリポジトリURLを指定するだけで、自動的に一時フォルダへクローンして分析を実行できます。手動で `git clone` する手間を省き、リモートのリポジトリを素早くコンテキスト化できます。
* **📁 フォルダツリーの生成** プロジェクトのフォルダとファイルの階層構造をツリー形式で 00\_ProjectTree.md に出力します。  
* **📄 統合されたコンテキストの生成** プロジェクト内の全ソースファイルの内容を、シンタックスハイライトと折りたたみ機能付きで Markdownファイル (01\_ProjectContext.md) に集約して出力します。大規模なプロジェクトの場合は自動的に複数ファイルに分割出力されます。  
* **⚙️ 柔軟な除外設定** .projectanalyzerignore ファイルを使用して、分析から除外したいファイルやフォルダを簡単に指定できます。また、bin obj .git などの一般的なフォルダはデフォルトで除外されます。  
* **💻 柔軟な利用形態 (CLI / DLL)** CLIツール（EXEファイル）としてスタンドアロンで実行できるほか、コアロジック（ProjectAnalyzer.Core）をDLLやNuGetパッケージとして自作のプロジェクトに組み込んで利用することも可能です。  
* **🧠 メモリ上での結果取得 (DLL利用時)** ファイルへの書き出しを行わず、分析結果のテキストデータをプログラム内で直接受け取ることができます。Markdownのコードブロック記号（\`\`\`）を省略するオプションも備えており、他のシステムとの連携が容易です。
* **🛡️ NotebookLM向けのHTML無害化機能**  --sanitize-html オプションを指定することで、出力結果に含まれるHTMLタグ（&lt;details>, &lt;div>など）を ＜details＞ のような無害な形式（全角）に変換し、AIが誤ってコードを解釈してしまうのを防ぎます。
* **⚡ ファイル単位の並列処理** 各ファイルのコンテンツ生成をファイル単位で並列実行し、マルチコアCPUを活用して分析を高速化します。特にOCR（--enable-ocr）のような重い処理を含む大量のファイルで効果が大きくなります。並列度は論理プロセッサ数に自動制限され、出力の順序・内容は逐次実行時と完全に一致します。
* **📎 非対応形式ファイルの自動スキップとコピー** PDFや画像などテキストとして読み込めないファイルは、文字化けした内容が出力へ混ざらないよう内容の抽出対象から自動的に除外し、原本を出力先の 02\_SkippedFiles/ フォルダへ元の階層構造のままコピーします。PDFなどはコピーされたファイルをそのままAIツールへアップロードできます。
* **🔗 シンボリックリンクを辿らない** シンボリックリンク（Windowsのジャンクションを含む）は内容の読み込み対象から除外します。リンク先が解析対象フォルダの外にあっても内容が出力へ混入せず、ループするリンクがあっても走査が終わらなくなることはありません。リンク自体はフォルダツリーに `[symbolic link, not followed]` の注記付きで表示されます。
* **📊 Wordの表の構造保持** Wordの表はMarkdownのテーブルとして抽出します。テキストボックスや図形の中に置かれた表も対象です。Markdownでは表現できない入れ子の表（表の中の表）は、参照を残したうえで外側の表の直後に独立したテーブルとして出力し、行と列の対応が失われないようにします。
* **✂️ 容量によるセクション単位の分割** NotebookLMなどのアップロード上限に対応するため、出力Markdownを --max-size で指定したしきい値に収まるよう分割します。分割はExcelのシート、PowerPointのスライド、Wordの見出しといったセクションの境界でのみ行われ、セクションの途中で内容が途切れることはありません。分割された各ファイルにはファイル名と相対パスが共通ヘッダとして再掲されます。

## **必要なもの**

* **.NET ランタイム または SDK** (**バージョン 8.0 以降**。v1.4.0 から .NET 6 / 7 はサポート対象外です / 開発やソースコードからの実行にはSDKが必要です)

### 📷 OCR機能（--enable-ocr）について

v1.4.0 から、画像内の文字抽出には [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR) の **PP-OCRv6 small** モデル（ONNX形式、約31MB）を [ONNX Runtime](https://onnxruntime.ai/) 上で実行します（.NETからの呼び出しには [RapidOcrNet](https://github.com/BobLd/RapidOcrNet) を使用）。日本語・英語を含む多言語を1つのモデルで認識します。

* **OS側への追加インストールは不要です。** Windows / Linux / macOS のいずれも、NuGetパッケージに含まれるライブラリとモデルだけで動作します。
* v1.3.2 以前で使用していた Tesseract は使用しなくなりました。以前のバージョンで必要だった Tesseract のインストール（apt / brew）や tessdata の配置は不要です。
* EMF/WMF などのベクター画像はOCRの対象外です（出力には `[OCR Skipped: unsupported image format]` と表示されます）。

> 💡 **パフォーマンスについて:** OCRはCPUを多く消費する処理ですが、本ツールはファイル単位で並列処理を行うため、マルチコア環境では処理時間を大幅に短縮できます（並列度は論理プロセッサ数に自動制限されます）。OCRエンジン内部のスレッドは1本に固定しているため、並列処理が二重になってかえって低速化することはありません。この設定はONNX Runtimeのセッション単位で行っており、プロセス全体の環境変数は変更しません。

#### ソースコードからビルドする場合
モデルファイル（`src/ProjectAnalyzer.Core/ocr-models/`）はリポジトリに含めていません。初回ビルド時に自動でダウンロードされ、SHA256ハッシュで検証されます（インターネット接続が必要です）。

## **⚠️ 制限事項**

* OCRモデル（ONNXファイル）は実行ファイルと同じ場所の `ocr-models/` フォルダから読み込みます。.NETの単一ファイル発行 (PublishSingleFile=true) を使う場合は、モデルとネイティブライブラリも1ファイルに含めるため、次のプロパティを併せて指定してください。
  ```bash
  dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true
  ```
* 縦書きの日本語は、文字は認識できますが列の読み順が正しく出力されない場合があります。

## **使い方**

### **1\. セットアップ**

CLIツールとして使用する場合は、[**Releases ページ**](https://github.com/flying-YT/ProjectAnalyzer/releases/latest) から実行ファイルをダウンロードするか、ソースコードをローカル環境に準備します。

| ダウンロードするファイル | 対象環境 |
| --- | --- |
| `ProjectAnalyzer-vX.Y.Z-win-x64.zip` | Windows (x64) |
| `ProjectAnalyzer-vX.Y.Z-linux-x64.zip` | Linux (x64) |

zipを展開し、中の `ProjectAnalyzer.Cli.exe`（Linuxでは `ProjectAnalyzer.Cli`）を実行します。.NETランタイムを同梱しているため、別途インストールは不要です。

ご自身のプロジェクトに組み込んで利用する場合は、ProjectAnalyzer.Core をNuGetパッケージ等から追加してください。

### **2\. 除外設定（任意）**

分析したくないファイルやフォルダがある場合は、分析対象プロジェクトのルートディレクトリに .projectanalyzerignore ファイルを作成し、除外対象を1行に1つずつ記述します。

* \# で始まる行はコメントとして無視されます。  
* **デフォルトで除外される主な項目:** bin, obj, .vs, .git, 出力フォルダ, .projectanalyzerignore

**.projectanalyzerignore の記述例:**

```text
# IDE/Editor specific
.vscode
.idea
.DS_Store

# Dependencies
node_modules
packages

# Build output
dist
```

### **開発環境別のおすすめ設定**

プロジェクトの種類に合わせて、以下の設定例を参考にしてください。

**Webフロントエンド（React / Vue / Next.js など）**

```text
node_modules
.next
.nuxt
dist
build
.cache
coverage
```

実行例（NotebookLM向け）:
```cmd
ProjectAnalyzer.Cli.exe --sanitize-html --no-codeblock
```

---

**ASP.NET Core / Webバックエンド**

```text
bin
obj
.vs
wwwroot/lib
Migrations
```

実行例:
```cmd
ProjectAnalyzer.Cli.exe "C:\MyApp" "C:\MyApp\output"
```

---

**Python プロジェクト**

> **注意:** Pythonはインデントが構文上重要なため、`--remove-indent` を使用するとNotebookLMでコードが正常に解釈されなくなる可能性があります。

```text
__pycache__
.venv
venv
.pytest_cache
*.egg-info
dist
build
```

実行例:
```cmd
ProjectAnalyzer.Cli.exe --sanitize-html --no-codeblock
```

---

**Node.js / TypeScript バックエンド**

```text
node_modules
dist
build
coverage
.nyc_output
```

実行例:
```cmd
ProjectAnalyzer.Cli.exe --sanitize-html --no-codeblock --remove-indent
```

---

### **3\. CLIツールとしての実行**

実行環境に合わせて、以下のいずれかの方法でコマンドを実行します。

引数を指定しない場合は、カレントディレクトリが分析対象となり、カレントディレクトリ内の output フォルダに出力されます。

**オプション:**
* `--no-codeblock`: 出力されるMarkdownファイル内のコードブロック記号（\`\`\`）を省略します。
* `--sanitize-html`: NotebookLMなどでコード内のHTMLタグが誤動作するのを防ぐため、&lt;details> などのタグを ＜details＞ に置換して出力します。ソースコード内の if (a < b) などは影響を受けません。
* `--remove-indent`: 行頭にあるインデント（スペースやタブ）をすべて削除します。インデントによるMarkdownコードブロックの誤解釈を防ぐために利用します（※Pythonなどインデントに意味がある言語の構造が壊れる可能性があるため注意してください）。
* `--per-file`: ファイルごとに個別のMarkdownファイルを出力します。
* `--enable-ocr`: このオプションを利用することで、Officeファイル内の画像もOCRを用いてテキストとして抽出することが可能になります。
* `--max-size <MB>`: 出力Markdown 1ファイルあたりのサイズのしきい値をMB単位で指定します（既定値は 4）。`--max-size 8` と `--max-size=8` のどちらの書き方でも指定できます。詳細は後述の「容量による分割について」を参照してください。

### **Wordの表の抽出について**

Wordの表は、行と列の対応が保たれるようMarkdownのテーブル記法へ変換します。本文中の表のほか、**テキストボックスや図形の中に置かれた表**、コンテンツコントロール内の表も対象です。

**入れ子の表（表の中の表）** はMarkdownのテーブル記法では表現できないため、次の形で出力します。

* セルには `[Nested Table 1]` という**参照だけ**を残します。
* 表の本体は、**外側の表の直後**に独立したテーブルとして出力し、`**[Nested Table 1]** (row 2, column 2)` という見出しでどのセルの中身かを示します。
* 番号は文書内の通し番号です。入れ子が3階層以上ある場合も、同じ規則で順に出力されます。
* 入れ子の表が無い文書の出力は、従来と変わりません。

**出力例:**

```text
| 機能ID | 機能名 | 入力項目 |
| --- | --- | --- |
| F-001 | 受注登録 | ※任意項目を含む<br>[Nested Table 1] |

**[Nested Table 1]** (row 2, column 3)

| 項目名 | 型 | 必須 |
| --- | --- | --- |
| 受注番号 | string | ○ |
| 受注日 | date | ○ |
```

> **補足:** ヘッダー・フッターに置かれた表は現在のところ抽出対象外です。

### **非対応形式のファイルの扱い**

PDFや画像などテキストとして読み込めないファイルは、そのまま読み込むと文字化けした内容が出力へ混ざり、AIへ渡すコンテキストを汚してしまいます。
そのため、これらのファイルは**内容の抽出対象から自動的に除外**したうえで、**原本を出力先へコピー**します。

| 種別 | 挙動 |
| --- | --- |
| ソースコード・テキスト | 従来どおり内容を抽出します |
| Word (.docx / .docm)・Excel (.xlsx / .xlsm / .xls)・PowerPoint (.pptx) | 専用の処理で内容を抽出します |
| 上記以外（PDF・画像・書庫・実行ファイルなど） | 内容を抽出せず、原本を 02\_SkippedFiles/ へコピーします |

* **原本は 02\_SkippedFiles/ フォルダへ、元の階層構造を保ったままコピーされます。** 同名のファイルが別のフォルダにあっても衝突せず、生成されたMarkdownとも混ざりません。
  *(例: docs/設計書.pdf → output/02\_SkippedFiles/docs/設計書.pdf)*
* **PDFなどはコピーされたファイルをそのままNotebookLMなどのAIツールへアップロードできます。** 本ツールはPDFの内容をテキスト化しませんが、AIツール側が対応していれば内容を読み取れます。
* **フォルダツリー (00\_ProjectTree.md) には従来どおり掲載されます。** また、コンテキスト側にも `[Skipped: unsupported binary file ...]` という1行のプレースホルダが出力されるため、AIが「ファイルは存在するが内容は渡されていない」ことを認識できます。
* **判定は拡張子と内容の両方で行われます。** 既知の拡張子（.pdf .png .zip .exe など）に加え、拡張子から判断できないファイルも先頭の内容を調べ、NULバイトを含む場合はバイナリと判定します。BOM付きのUTF-16/UTF-32のテキストは、NULバイトを含んでいてもテキストとして扱われます。
* **旧形式のOfficeファイル（.doc / .ppt）やRTFは非対応形式として扱われます。** 内容の抽出は行わず、原本のコピーのみを行います。
* **ファイル出力を行わない場合（DLL利用時の outputToFile: false）はコピーされません。** スキップされたファイルの相対パスは `AnalyzerResult.SkippedFiles` から取得できます。
* 特定のファイルを最初から対象外にしたい場合は、従来どおり .projectanalyzerignore に記述してください。

### **容量による分割について**

出力MarkdownをNotebookLMなどへアップロードする際、1ソースあたりの上限に引っかかることがあります。
これを避けるため、しきい値を超えた場合にファイルを分割して出力します。

分割は以下の**両方**を満たしたときにだけ行われます。

1. レンダリング後の容量が `--max-size` のしきい値を超える
2. セクションが2つ以上ある

| 種別 | 分割の単位 |
| --- | --- |
| Excel (.xlsx / .xlsm / .xls) | シート |
| PowerPoint (.pptx) | スライド |
| Word (.docx / .docm) | 見出し1（H1） |
| Word（見出しスタイル未使用）・テキスト・ソースコード | 分割しません |

* **セクションの途中では分割しません。** 1つのシートやスライドだけでしきい値を超える場合は、途中で切らずにそのまま出力します。そのため、しきい値は保証ではなく努力目標です。
* **プレーンテキストやソースコードは分割対象外です。** 構造を推測して分割すると、ソースコード中の `### コメント` のような行を見出しと誤検出してしまうためです。
* **分割された各ファイルには、ファイル名と相対パス、`**Part:** 2/3` のようなパート表記が共通ヘッダとして再掲されます。** また `<details>` タグとコードブロック記号は各パート内で必ず開いて閉じられるため、開始タグだけ・終了タグだけのファイルは生成されません。
* `--per-file` と併用した場合、分割されたファイルは `Book.xlsx.1.md` `Book.xlsx.2.md` のように連番が付きます。分割されなかったファイルの名前は `Book.xlsx.md` のまま変わりません。
* コードブロック記号を省略しない場合、同じ内容が `<details>` 内とコードブロック内に2回出力されるため、しきい値に対して実質半分の内容しか入りません。分割を利用する際は `--no-codeblock` の併用をおすすめします。

*   **A. EXEファイルから実行する場合 (Windows)**

    配布されている ProjectAnalyzer.Cli.exe を使用します。

    ```cmd
    # 基本的な使い方 (カレントディレクトリを分析)
    ProjectAnalyzer.Cli.exe

    # コードブロック記号を省略する場合
    ProjectAnalyzer.Cli.exe --no-codeblock

    # コードブロック記号を省略し、AIツール向けに記号とインデントを取り除く場合
    ProjectAnalyzer.Cli.exe --no-codeblock --sanitize-html --remove-indent

    # パスを指定して実行
    ProjectAnalyzer.Cli.exe "[分析したいプロジェクトのパス]" "[出力先のパス]"

    # GitHubリポジトリを直接分析
    ProjectAnalyzer.Cli.exe "https://github.com/username/repository.git"

    # プライベートリポジトリの場合 (アクセストークンを含める)
    ProjectAnalyzer.Cli.exe "https://<YOUR_TOKEN>@github.com/username/repository.git"
    ```

*   **B. ソースコードから実行する場合 (.NET SDK環境 / クロスプラットフォーム)**

    ターミナルを開き、このツールの ProjectAnalyzer.Cli プロジェクトディレクトリ内で以下のコマンドを実行します。

    ```bash
    # 基本的な使い方
    dotnet run

    # コードブロック記号を省略する場合
    dotnet run -- --no-codeblock

    # コードブロック記号を省略し、AIツール向けに記号とインデントを取り除く場合
    dotnet run -- --no-codeblock --sanitize-html --remove-indent

    # パスを指定して実行
    dotnet run -- "[分析したいプロジェクトのパス]" "[出力先のパス]"

    # GitHubリポジトリを直接分析
    dotnet run -- "https://github.com/username/repository.git"

    # プライベートリポジトリの場合 (アクセストークンを含める)
    dotnet run -- "https://<YOUR_TOKEN>@github.com/username/repository.git"
    ```

    **パス指定の実行例:**

    ```bash
    dotnet run -- "C:\path\to\your\project" "C:\path\to\output"
    ```

### **4\. 自作プロジェクトへの組み込み (ProjectAnalyzer.Core の利用)**

ProjectAnalyzer.Core はクラスライブラリ（DLL）として提供されています。NuGetパッケージ等から自身のプロジェクトに追加することで、C\#のコード内から直接アナライザーを呼び出して利用することができます。

**基本的な実装例 (ファイルへ出力する場合):**

```csharp
using ProjectAnalyzer.Core;

// 1. 設定の読み込み (分析対象のパスと出力先のパスを指定)
var settings = SettingsLoader.Load("C:\\path\\to\\your\\project", "C:\\path\\to\\output");

// 2. 分析処理の実行
using var analyzer = new Analyzer(settings);
AnalyzerResult result = analyzer.Analyze(); // ファイル出力と同時に結果オブジェクトも返ります
```

**高度な実装例 (ファイル出力せず、メモリ上でテキストを受け取る場合):**

DLLとして組み込む際、ファイルI/Oを発生させずに分析結果の文字列だけを取得し、Markdownのコードブロック（\`\`\`）も不要な場合は、引数でフラグを指定します。

````csharp
using ProjectAnalyzer.Core;

// outputToFile: false にするとファイル出力をスキップします。
// omitCodeBlockTicks: true にすると Markdownの ``` プログラムコード ``` の部分を省略します。
var settings = SettingsLoader.Load(
    projectPath: "C:\\path\\to\\your\\project",
    outputPath: "", // 出力しない場合は空で構いません
    outputToFile: false,
    omitCodeBlockTicks: true,
    sanitizeHtmlTags: true,
    removeIndent: true,
    // 1ファイルあたりのサイズのしきい値（バイト単位／既定は 4MB）。
    // 超えた場合はセクション単位で分割されます。
    maxOutputSize: 8 * 1024 * 1024
);

using var analyzer = new Analyzer(settings);
AnalyzerResult result = analyzer.Analyze();

// 結果をプログラム内で自由に利用できます
Console.WriteLine(result.ProjectTree); // ツリー構造の文字列

foreach (var context in result.ProjectContexts)
{
    // 各ファイルの内容をまとめた文字列（分割されている場合は複数要素）
    Console.WriteLine(context);
}
````

## **出力結果 (ファイル出力有効時)**

実行後、指定した出力先フォルダ（デフォルトでは output）に以下のファイルが生成されます。

* 00\_ProjectTree.md: プロジェクト全体のフォルダ構成をツリー形式で示します。  
* 01\_ProjectContext.md: プロジェクト内のすべてのファイルの内容を一つにまとめたMarkdownファイルです。各ファイルは相対パスと共に記載され、コードブロックはシンタックスハイライト付きで表示されます。  
  *(※プロジェクトのファイルサイズが大きい場合、自動的に 01\_ProjectContext\_1.md, 01\_ProjectContext\_2.md ... のように分割して出力されます)*
* `--per-file` を指定した場合は、01\_ProjectContexts/ フォルダ以下に元の階層構造を保ったまま、ファイルごとのMarkdownが出力されます。  
  *(※しきい値を超えたファイルは Book.xlsx.1.md, Book.xlsx.2.md ... のように連番付きで分割されます)*
* 02\_SkippedFiles/: PDFや画像など、内容をテキストとして抽出できなかったファイルの原本が、元の階層構造を保ったままコピーされます。該当するファイルが無い場合はフォルダ自体が作成されません。詳細は「非対応形式のファイルの扱い」を参照してください。

## **プロジェクトの構造**

このツールは、責務の分離原則に基づいたシンプルなアーキテクチャで構成されており、コアロジック（Core）とコンソールアプリ（Cli）に分かれています。

### **ProjectAnalyzer.Cli (エントリーポイント)**

* Program.cs: アプリケーションのエントリーポイント。コマンドライン引数を解釈し、分析処理を起動します。

### **ProjectAnalyzer.Core (コアロジック)**

* Analyzer.cs: 分析処理全体を統括するオーケストレーターです。  
* AnalyzerResult.cs: 分析結果（ツリーテキストやコンテキストテキストのリスト）を保持するクラスです。  
* AnalyzerSettings.cs: 分析対象のパス情報、除外リスト、出力制御フラグなどの設定を保持します。  
* SettingsLoader.cs: .projectanalyzerignore ファイルを読み込み、デフォルト設定とマージして設定オブジェクトを生成します。  
* TreeGenerator.cs: 00\_ProjectTree.md 用のフォルダ構成ツリーを生成します。  
* FileContentGenerator.cs: 全てのファイルの内容を読み込み、01\_ProjectContext.md 用のコンテンツを生成します。  
* LanguageMapper.cs: ファイルの拡張子を、Markdownのシンタックスハイライトで使われる言語識別子にマッピングします。
* BinaryFileDetector.cs: PDFや画像など、テキストとして読み込めないファイルを判定します。

## **ライセンス**

このプロジェクトは **MITライセンス** の下で公開されています。

## **謝辞**

* **[ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader)** (MIT License)
* **[DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK)** (MIT License)
* **[PaddleOCR / PP-OCRv6](https://github.com/PaddlePaddle/PaddleOCR)** (Apache License 2.0) ※OCRモデルはNuGetパッケージに同梱して再配布しています
* **[RapidOcrNet](https://github.com/BobLd/RapidOcrNet)** (Apache License 2.0)
* **[RapidOCR](https://github.com/RapidAI/RapidOCR)** (Apache License 2.0)
* **[ONNX Runtime](https://github.com/microsoft/onnxruntime)** (MIT License)
* **[SkiaSharp](https://github.com/mono/SkiaSharp)** (MIT License)