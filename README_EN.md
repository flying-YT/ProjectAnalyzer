# **Project Analyzer**

[![NuGet Version](https://img.shields.io/nuget/v/ProjectAnalyzer.Core.svg)](https://www.nuget.org/packages/ProjectAnalyzer.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ProjectAnalyzer.Core.svg)](https://www.nuget.org/packages/ProjectAnalyzer.Core)

## **Overview**

**Project Analyzer** is a .NET-based command-line tool and class library that analyzes the structure and content of a specified project folder **or GitHub repository** and outputs it as a Markdown file, making it easily consumable as context for AI (LLMs).

It is especially useful when using a **GitHub repository** or **your own project folder** as a source for AI tools like **NotebookLM**.

Furthermore, it supports reading not only source code but also **Office software files such as Word, Excel, and PowerPoint**. This allows you to provide the AI with the context of the entire project, including specifications and design documents.

## **Key Features**

* **🐙 Direct GitHub Repository Analysis:** Simply specify a GitHub repository URL, and it will automatically clone it to a temporary folder and run the analysis. This saves you the trouble of manually running `git clone` and allows you to quickly contextualize remote repositories.
* **📁 Folder Tree Generation:** Outputs the hierarchical structure of the project's folders and files in a tree format to `00_ProjectTree.md`.
* **📄 Integrated Context Generation:** Aggregates the contents of all source files in the project into a single Markdown file (`01_ProjectContext.md`) with syntax highlighting and folding capabilities. For large projects, it automatically splits the output into multiple files.
* **⚙️ Flexible Exclusion Settings:** You can easily specify files or folders to exclude from the analysis using a `.projectanalyzerignore` file. Common folders like `bin`, `obj`, and `.git` are excluded by default.
* **💻 Flexible Usage (CLI / DLL):** It can be run standalone as a CLI tool (EXE file), or you can integrate the core logic (`ProjectAnalyzer.Core`) into your own projects as a DLL or NuGet package.
* **🧠 In-Memory Result Retrieval (When using DLL):** You can directly receive the analyzed text data in memory without writing to a file. It also provides an option to omit Markdown code block backticks (\`\`\`), making integration with other systems easy.
* **🛡️ HTML Sanitization for NotebookLM:** By specifying the `--sanitize-html` option, HTML tags (e.g., `<details>`, `<div>`) included in the output are converted to a harmless format (full-width characters like `＜details＞`) to prevent AI from misinterpreting the code.

## **Requirements**

* **.NET Runtime or SDK** (version 6.0 or later / SDK is required for development or running from source code)

### 📷 Prerequisites for the OCR Feature (--enable-ocr)

When using the `--enable-ocr` option to extract text from images, you may need to pre-install the Tesseract engine depending on your operating system.

#### 1. OS-Specific Requirements

**🪟 Windows**
* No additional OS-level installation is required. It works automatically using the libraries included in the NuGet package.

**🐧 Linux (Ubuntu / Debian)**
* You need to install the core OCR engine and Japanese language data. Run the following commands in your terminal:
  ```bash
  sudo apt-get update
  sudo apt-get install -y tesseract-ocr libtesseract-dev libleptonica-dev tesseract-ocr-jpn

**🍎 macOS**
* Use Homebrew to install Tesseract and its language data:
  ```bash
  brew install tesseract tesseract-lang

#### 2. Placing Trained Data (tessdata)
If you are building and running from source, create a tessdata folder in your execution directory (or src/ProjectAnalyzer.Core/) and place the following trained models inside:
* [jpn.traineddata (Japanese)](https://github.com/tesseract-ocr/tessdata/blob/main/jpn.traineddata)
* [eng.traineddata (English)](https://github.com/tesseract-ocr/tessdata/blob/main/eng.traineddata)

Note: If the native library fails to load on Linux/macOS, a fallback mechanism will automatically activate and use the system-installed tesseract command.

## **Usage**

### **1. Setup**

To use it as a CLI tool, download the distributed executable (`ProjectAnalyzer.Cli.exe`) or prepare the source code in your local environment.

To integrate it into your own project, add `ProjectAnalyzer.Core` via NuGet or other package managers.

### **2. Exclusion Settings (Optional)**

If there are files or folders you do not want to analyze, create a `.projectanalyzerignore` file in the root directory of the target project and write the items to exclude, one per line.

* Lines starting with `#` are ignored as comments.
* **Items excluded by default:** `bin`, `obj`, `.vs`, `.git`, output folders, `.projectanalyzerignore`

**Example `.projectanalyzerignore`:**

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

### **3. Running as a CLI Tool**

Execute one of the following commands depending on your environment.

If no arguments are provided, the current directory will be analyzed, and the results will be output to an `output` folder within the current directory.

**Options:**
* `--no-codeblock`: Omits Markdown code block backticks (\`\`\`) in the output files.
* `--sanitize-html`: Replaces tags like `<details>` with `＜details＞` to prevent HTML tags in the code from malfunctioning in tools like NotebookLM. Syntax in source code like `if (a < b)` is not affected.
* `--remove-indent`: Removes all leading indentation (spaces or tabs). Used to prevent misinterpretation of Markdown code blocks due to indentation (*Note: Be careful as this may break the structure of languages where indentation is meaningful, like Python*).
* `--per-file`: Outputs individual Markdown files for each analyzed file.

*   **A. Running from the EXE file (Windows)**

    Use the distributed `ProjectAnalyzer.Cli.exe`.

    ```cmd
    # Basic usage (analyzes the current directory)
    ProjectAnalyzer.Cli.exe

    # Omitting code block backticks
    ProjectAnalyzer.Cli.exe --no-codeblock

    # Omitting code blocks and removing indentation for AI tools
    ProjectAnalyzer.Cli.exe --no-codeblock --sanitize-html --remove-indent

    # Specifying paths
    ProjectAnalyzer.Cli.exe "[Path to project]" "[Path to output]"

    # Directly analyzing a GitHub repository
    ProjectAnalyzer.Cli.exe "https://github.com/username/repository.git"

    # For a private repository (include the access token)
    ProjectAnalyzer.Cli.exe "https://<YOUR_TOKEN>@github.com/username/repository.git"
    ```

*   **B. Running from Source Code (.NET SDK Environment / Cross-platform)**

    Open a terminal and run the following commands within the `ProjectAnalyzer.Cli` project directory.

    ```bash
    # Basic usage
    dotnet run

    # Omitting code block backticks
    dotnet run -- --no-codeblock

    # Omitting code blocks and removing indentation for AI tools
    dotnet run -- --no-codeblock --sanitize-html --remove-indent

    # Specifying paths
    dotnet run -- "[Path to project]" "[Path to output]"

    # Directly analyzing a GitHub repository
    dotnet run -- "https://github.com/username/repository.git"

    # For a private repository (include the access token)
    dotnet run -- "https://<YOUR_TOKEN>@github.com/username/repository.git"
    ```

    **Path Specification Example:**

    ```bash
    dotnet run -- "C:\path\to\your\project" "C:\path\to\output"
    ```

### **4. Integrating into Your Own Project (Using ProjectAnalyzer.Core)**

`ProjectAnalyzer.Core` is provided as a class library (DLL). By adding it to your project via NuGet, you can call and utilize the analyzer directly from your C# code.

**Basic Implementation Example (Outputting to files):**

```csharp
using ProjectAnalyzer.Core;

// 1. Load settings (specify target project path and output path)
var settings = SettingsLoader.Load("C:\\path\\to\\your\\project", "C:\\path\\to\\output");

// 2. Execute analysis
using var analyzer = new Analyzer(settings);
AnalyzerResult result = analyzer.Analyze(); // Result object is returned along with file output
```

**Advanced Implementation Example (Getting text in memory without file output):**

When integrating as a DLL, if you want to obtain only the analysis result string without triggering file I/O, and if you also do not need Markdown code blocks (\`\`\`), specify the flags in the arguments.

````csharp
using ProjectAnalyzer.Core;

// Set outputToFile: false to skip file output.
// Set omitCodeBlockTicks: true to omit Markdown ``` program code ``` blocks.
var settings = SettingsLoader.Load(
    projectPath: "C:\\path\\to\\your\\project",
    outputPath: "", // Can be empty if not outputting
    outputToFile: false,
    omitCodeBlockTicks: true,
    sanitizeHtmlTags: true,
    removeIndent: true
);

using var analyzer = new Analyzer(settings);
AnalyzerResult result = analyzer.Analyze();

// The results can be used freely within your program
Console.WriteLine(result.ProjectTree); // String of the tree structure

foreach (var context in result.ProjectContexts)
{
    // String containing the content of each file (multiple elements if split)
    Console.WriteLine(context);
}
````

## **Output Results (When file output is enabled)**

After execution, the following files are generated in the specified output folder (`output` by default).

* `00_ProjectTree.md`: Shows the entire project folder structure in a tree format.
* `01_ProjectContext.md`: A Markdown file that combines the contents of all files in the project. Each file is listed with its relative path, and code blocks are displayed with syntax highlighting.
  *(Note: If the project file size is large, it will automatically be split into `01_ProjectContext_1.md`, `01_ProjectContext_2.md`, etc.)*

## **Project Structure**

This tool is built on a simple architecture based on the separation of concerns principle, divided into core logic (Core) and a console app (Cli).

### **ProjectAnalyzer.Cli (Entry Point)**

* `Program.cs`: The entry point of the application. Parses command-line arguments and initiates the analysis process.

### **ProjectAnalyzer.Core (Core Logic)**

* `Analyzer.cs`: The orchestrator that manages the entire analysis process.
* `AnalyzerResult.cs`: A class that holds the analysis results (tree text and context text list).
* `AnalyzerSettings.cs`: Holds settings such as target path information, exclusion lists, and output control flags.
* `SettingsLoader.cs`: Reads the `.projectanalyzerignore` file, merges it with default settings, and generates the settings object.
* `TreeGenerator.cs`: Generates the folder structure tree for `00_ProjectTree.md`.
* `FileContentGenerator.cs`: Reads the contents of all files and generates the content for `01_ProjectContext.md`.
* `LanguageMapper.cs`: Maps file extensions to language identifiers used in Markdown syntax highlighting.

## **License**

This project is released under the **MIT License**.

## **Acknowledgments**

* **ExcelDataReader** (MIT License)
* **DocumentFormat.OpenXml** (MIT License)
