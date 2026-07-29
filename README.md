# **Project Analyzer**

[![NuGet Version](https://img.shields.io/nuget/v/ProjectAnalyzer.Core.svg)](https://www.nuget.org/packages/ProjectAnalyzer.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ProjectAnalyzer.Core.svg)](https://www.nuget.org/packages/ProjectAnalyzer.Core)

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
* **✂️ 容量によるセクション単位の分割** NotebookLMなどのアップロード上限に対応するため、出力Markdownを --max-size で指定したしきい値に収まるよう分割します。分割はExcelのシート、PowerPointのスライド、Wordの見出しといったセクションの境界でのみ行われ、セクションの途中で内容が途切れることはありません。分割された各ファイルにはファイル名と相対パスが共通ヘッダとして再掲されます。

## **必要なもの**

* **.NET ランタイム または SDK** (バージョン 6.0 以降 / 開発やソースコードからの実行にはSDKが必要です)

### 📷 OCR機能（--enable-ocr）を利用するための事前準備

画像内の文字抽出を行う `--enable-ocr` オプションを利用する場合、実行するOS環境によってはTesseractエンジンの事前インストールが必要です。

> 💡 **パフォーマンスについて:** OCRはCPUを多く消費する処理ですが、本ツールはファイル単位で並列処理を行うため、マルチコア環境では処理時間を大幅に短縮できます（並列度は論理プロセッサ数に自動制限されます）。

> ⚙️ **ライブラリ（DLL）として組み込む場合の注意 ― OpenMPスレッド設定:** OCRを有効にすると、本ライブラリは初回利用時にプロセス全体の環境変数 `OMP_THREAD_LIMIT=1` を自動的に設定します。これは、ファイル単位の並列処理とTesseract内部の並列処理（1画像あたり全コアを使用）が競合してかえって低速化する「オーバーサブスクリプション」を防ぐためです。通常はそのままで問題ありませんが、**同一プロセス内で他のOpenMPベースのライブラリ（数値計算・画像処理など）も利用しており、そのスレッド数をご自身で管理したい場合**は、本ライブラリを利用する前に `OMP_THREAD_LIMIT` または `OMP_NUM_THREADS` を設定してください。いずれかが既に設定済みの場合、本ライブラリは値を上書きしません。

#### 1. OSごとの必須要件

**🪟 Windows 環境**
* OS側への追加インストールは不要です。NuGetパッケージに含まれるライブラリで自動的に動作します。

**🐧 Linux (Ubuntu / Debian) 環境**
* OCRエンジン本体と日本語データのインストールが必要です。ターミナルで以下のコマンドを実行してください。
  ```bash
  sudo apt-get update
  sudo apt-get install -y tesseract-ocr libtesseract-dev libleptonica-dev tesseract-ocr-jpn

**🍎 macOS 環境**
* Homebrewを使用してTesseract本体と多言語データをインストールしてください。
  ```bash
  brew install tesseract tesseract-lang

#### 2. 学習済みデータ (tessdata) の配置について
ソースコードからビルド・実行する場合は、実行ディレクトリ（または src/ProjectAnalyzer.Core/）に tessdata フォルダを作成し、以下の学習済みモデルを配置してください。
* [jpn.traineddata (日本語)](https://github.com/tesseract-ocr/tessdata/blob/main/jpn.traineddata)
* [eng.traineddata (英語)](https://github.com/tesseract-ocr/tessdata/blob/main/eng.traineddata)

※Linux/macOS環境でネイティブライブラリの読み込みに失敗した場合は、自動的にOSにインストールされた tesseract コマンドを使用するフォールバック機能が作動します。

## **⚠️ 制限事項**

本ライブラリはOCR処理に Tesseract を使用しており、実行時に物理的なネイティブDLL（x64/ 等）と学習データ（tessdata/）を必要とします。そのため、.NETの 単一ファイル発行 (PublishSingleFile=true) には対応していません。ビルドや発行を行う際は、通常の形式（複数ファイルが出力される形式）でご利用ください。

## **使い方**

### **1\. セットアップ**

CLIツールとして使用する場合は、配布されている実行ファイル（ProjectAnalyzer.Cli.exe）をダウンロードするか、ソースコードをローカル環境に準備します。

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
| Word (.docx) | 見出し1（H1） |
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
    rojectAnalyzer.Cli.exe --no-codeblock --sanitize-html --remove-indent

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

## **ライセンス**

このプロジェクトは **MITライセンス** の下で公開されています。

## **謝辞**

* **[ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader)** (MIT License)
* **[DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK)** (MIT License)
* **[Tesseract](https://github.com/charlesw/tesseract/)** (Apache License 2.0)
* **[tessdata](https://github.com/tesseract-ocr/tessdata)** (Apache License 2.0)