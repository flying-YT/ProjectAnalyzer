# **Project Analyzer**

## **概要**

**Project Analyzer** は、指定されたプロジェクトフォルダ**またはGitHubリポジトリ**の構造と内容を分析し、AI (LLM) のコンテキストとして利用しやすいように、Markdownファイルとして出力する.NET製のコマンドラインツールおよびクラスライブラリです。

主に**Githubのリポジトリ**や**自作のプロジェクトフォルダ**を**NotebookLM**などのAIツールのソースとして使う際に活用できます。

## **主な機能**

* **🐙 GitHubリポジトリの直接分析** GitHubのリポジトリURLを指定するだけで、自動的に一時フォルダへクローンして分析を実行できます。手動で `git clone` する手間を省き、リモートのリポジトリを素早くコンテキスト化できます。
* **📁 フォルダツリーの生成** プロジェクトのフォルダとファイルの階層構造をツリー形式で 00\_ProjectTree.md に出力します。  
* **📄 統合されたコンテキストの生成** プロジェクト内の全ソースファイルの内容を、シンタックスハイライトと折りたたみ機能付きで Markdownファイル (01\_ProjectContext.md) に集約して出力します。大規模なプロジェクトの場合は自動的に複数ファイルに分割出力されます。  
* **⚙️ 柔軟な除外設定** .projectanalyzerignore ファイルを使用して、分析から除外したいファイルやフォルダを簡単に指定できます。また、bin obj .git などの一般的なフォルダはデフォルトで除外されます。  
* **💻 柔軟な利用形態 (CLI / DLL)** CLIツール（EXEファイル）としてスタンドアロンで実行できるほか、コアロジック（ProjectAnalyzer.Core）をDLLやNuGetパッケージとして自作のプロジェクトに組み込んで利用することも可能です。  
* **🧠 メモリ上での結果取得 (DLL利用時)** ファイルへの書き出しを行わず、分析結果のテキストデータをプログラム内で直接受け取ることができます。Markdownのコードブロック記号（\`\`\`）を省略するオプションも備えており、他のシステムとの連携が容易です。

## **必要なもの**

* **.NET ランタイム または SDK** (バージョン 9.0 以降 / 開発やソースコードからの実行にはSDKが必要です)

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

### **3\. CLIツールとしての実行**

実行環境に合わせて、以下のいずれかの方法でコマンドを実行します。

引数を指定しない場合は、カレントディレクトリが分析対象となり、カレントディレクトリ内の output フォルダに出力されます。

*   **A. EXEファイルから実行する場合 (Windows)**

    配布されている ProjectAnalyzer.Cli.exe を使用します。

    ```cmd
    # 基本的な使い方 (カレントディレクトリを分析)
    ProjectAnalyzer.Cli.exe

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
    omitCodeBlockTicks: true
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