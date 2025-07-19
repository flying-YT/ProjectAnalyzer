# **Project Analyzer**

## **概要**

Project Analyzer は、指定されたプロジェクトフォルダの構造と内容を分析し、ドキュメントとしてMarkdown形式で出力する.NET製のコマンドラインツールです。 プロジェクト全体の概要を把握したり、コードレビューの資料を作成したり、他者とソースコードの構成を共有する際に役立ちます。

## **主な機能**

* **フォルダツリーの生成**: プロジェクトのフォルダとファイルの階層構造をツリー形式で出力します。  
* **ファイル内容のエクスポート**: 各ソースファイルの内容を、シンタックスハイライト付きのMarkdownファイルとして個別に書き出します。  
* **除外設定**: .projectanalyzerignore ファイルを使用して、分析から除外したい特定のファイルやフォルダを簡単に指定できます。  
* **クロスプラットフォーム**: .NETが動作するWindows, macOS, Linuxの各環境で利用できます。

## **必要なもの**

* .NET SDK (バージョン 6.0 以降)

## **使い方**

1. **セットアップ** このツール（ProjectAnalyzer）のソースコードをローカルに準備します。  
2. **除外設定（任意）** 分析したくないファイルやフォルダがある場合は、分析対象のプロジェクトフォルダのルートに .projectanalyzerignore という名前のファイルを作成します。 このファイルに、除外したいファイル名またはフォルダ名を1行に1つずつ記述します。\# で始まる行はコメントとして扱われます。例: .projectanalyzerignore  
   `# IDE/Editor specific`  
   `.vscode`  
   `.vs`  
   `.DS_Store`

   `# Dependencies`  
   `node_modules`  
   `packages`

   `# Build output`  
   `bin`  
   `obj`  
   `dist`

3. **分析の実行** ターミナルを開き、ProjectAnalyzer のプロジェクトディレクトリで dotnet run コマンドを実行します。基本的な使い方:  
   `dotnet run`  
   パスを指定して実行:  
   `dotnet run -- "C:\path\to\your\project" "C:\path\to\output"`

## **出力結果**

実行後、指定した出力先フォルダ（デフォルトでは output）に以下のようなファイルが生成されます。

* 00\_ProjectTree.md: プロジェクト全体のフォルダ構成ツリー。  
* \[元のファイルパス\].md: 各ファイルの内容が書き出されたMarkdownファイル。

## **プロジェクトの構造**

このツールは、責務の分離原則に基づいたクリーンなアーキテクチャで構成されています。

* Program.cs: アプリケーションのエントリーポイント。  
* ProjectAnalyzer.cs: 分析処理全体を統括するオーケストレーター。  
* AnalyzerSettings.cs: パス情報や除外リストなどの設定を保持。  
* SettingsLoader.cs: .projectanalyzerignore を読み込み、設定オブジェクトを生成。  
* TreeGenerator.cs: フォルダ構成ツリーの生成ロジックを担当。  
* FileContentGenerator.cs: ファイル内容をMarkdownとして書き出す処理を担当。  
* LanguageMapper.cs: 拡張子を言語識別子にマッピング。

## **ライセンス**

このプロジェクトはMITライセンスの下で公開されています。