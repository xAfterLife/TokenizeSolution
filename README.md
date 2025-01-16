
# TokenizeSolution

## Overview
**TokenizeSolution** is your go-to tool for transforming complex codebases into clean, tokenized directory layouts, perfectly suited for AI analysis. By efficiently ignoring unnecessary files and directories, **TokenizeSolution** ensures a streamlined view of your project, enabling faster and more accurate AI processing. Whether you're prepping a large-scale solution or a smaller project, this tool provides clarity and structure, saving you time and effort.

## Why Choose TokenizeSolution?
- **AI-Ready Structure**: Automatically organizes your project for seamless AI integration.
- **Time-Saving Automation**: Ignores clutter like `bin`, `obj`, and `.git` directories.
- **Customizable**: Add your own ignoring rules to fine-tune the process.
- **AOT Compile Friendly**: Built for performance and compatibility, including Ahead-Of-Time (AOT) compilation.
- **Executable Convenience**: Download ready-to-use executables from the Releases section. No need for .NET runtime—just run the EXE with your arguments!

## Fun Fact
This README, along with other essential project files, was generated using **TokenizeSolution** itself. Talk about eating your own dog food!

## Table of Contents
1. [Installation](#installation)
2. [Usage](#usage)
3. [Options](#options)
4. [Examples](#examples)
5. [Releases](#releases)
6. [Contributing](#contributing)
7. [License](#license)

## Installation

### Prerequisites
- .NET SDK 9.0.0 or later (if building from source)

### Clone the Repository
```bash
git clone https://github.com/yourusername/TokenizeSolution.git
cd TokenizeSolution
```

### Build the Project
```bash
dotnet build
```

## Usage
Running **TokenizeSolution** is simple:

```bash
dotnet run --project TokenizeSolution/TokenizeSolution.csproj <solution-path> <output-file> [options]
```

Or, if you're using the EXE from the Releases section:

```bash
TokenizeSolution.exe <solution-path> <output-file> [options]
```

### Parameters
- `<solution-path>`: The path to the solution you want to tokenize.
- `<output-file>`: The output file path where the directory layout will be saved.

## Options
- `--ignore-dir <directory>`: Specify an additional directory to ignore.
- `--ignore-file <file>`: Specify an additional file to ignore.
- `--help`: Display help information.

## Examples

### Basic Usage
```bash
TokenizeSolution.exe ./MyProject ./layout.txt
```

### Ignoring Custom Directory and File
```bash
TokenizeSolution.exe ./MyProject ./layout.txt --ignore-dir custom-bin --ignore-file *.tmp
```

## Releases
Head over to the [Releases](https://github.com/yourusername/TokenizeSolution/releases) section to download the latest EXE files. These executables are pre-compiled and ready to run—no need to install .NET. Just open them with your desired arguments and you're good to go!

## Contributing
We love contributions! If you have ideas or improvements, fork the repository and submit a pull request. Let's build something amazing together.

## License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for more information.
