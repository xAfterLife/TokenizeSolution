using System.Text;
using System.Text.RegularExpressions;

namespace TokenizeSolution;

public static class Program
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common build and IDE directories
        "bin", "obj", ".vs", ".idea", ".vscode", ".vsconfig", ".vspscc", ".suo", ".user",
        // Dependency directories
        "packages", "node_modules", "bower_components", "jspm_packages", "typings",
        // Version control directories
        ".git", ".svn", ".hg",
        // Environment and configuration files
        ".env", ".env.local", ".env.development", ".env.production", ".env.test",
        // Temporary and cache files
        "temp", "tmp", "cache", ".cache",
        // Log files
        "logs", "*.log",
        // OS-specific files
        ".DS_Store", "Thumbs.db", "desktop.ini",
        // Project-specific files
        "*.userprefs", "*.sln.cache", "*.suo", "*.lock"
    };

    private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common files to ignore
        ".gitignore", ".gitattributes", ".gitmodules", ".gitkeep",
        ".npmrc", ".yarnrc", ".editorconfig", ".eslintrc", ".prettierrc",
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        "*.tmp", "*.bak", "*.swp", "*.swo", "*.log", "*.pid", "*.seed", "*.pid.lock"
    };

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common binary file extensions
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", // Images
        ".mp3", ".wav", ".ogg", ".flac", ".aac", // Audio
        ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", // Video
        ".zip", ".rar", ".tar", ".gz", ".7z", // Archives
        ".exe", ".dll", ".pdb", ".so", ".dylib", ".lib", // Binaries
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", // Office files
        ".ttf", ".otf", ".woff", ".woff2", ".eot", // Fonts
        ".res", ".resx" // Resource Files
    };

    public static async Task Main(string[] args)
    {
        if ( args.Length == 0 || args.Contains("--help") )
        {
            ShowHelp();
            return;
        }

        if ( args.Length < 2 )
        {
            Console.WriteLine("Error: Invalid number of arguments.");
            ShowHelp();
            return;
        }

        var solutionPath = args[0];
        var outputFile = args[1];

        // Parse additional ignored directories and files
        var additionalIgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additionalIgnoredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for ( var i = 2; i < args.Length; i++ )
            switch ( args[i] )
            {
                case "--ignore-dir" when i + 1 < args.Length:
                    additionalIgnoredDirectories.Add(args[i + 1]);
                    i++;
                    break;
                case "--ignore-file" when i + 1 < args.Length:
                    additionalIgnoredFiles.Add(args[i + 1]);
                    i++;
                    break;
            }

        try
        {
            var startTime = DateTime.UtcNow;
            await GenerateLayoutAsync(solutionPath, outputFile, additionalIgnoredDirectories, additionalIgnoredFiles);
            var endTime = DateTime.UtcNow;

            Console.WriteLine($"Elapsed time: {(endTime - startTime).TotalMilliseconds} ms.");
        }
        catch ( Exception ex )
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task GenerateLayoutAsync(string solutionPath, string outputFile,
        HashSet<string> additionalIgnoredDirectories, HashSet<string> additionalIgnoredFiles)
    {
        var sb = new StringBuilder();
        var gitignoreRules = LoadGitignoreRules(solutionPath);
        var gitignoreRegexes = CompileGitignoreRules(gitignoreRules);

        var allIgnoredDirectories = new HashSet<string>(IgnoredDirectories, StringComparer.OrdinalIgnoreCase);
        allIgnoredDirectories.UnionWith(additionalIgnoredDirectories);

        var allIgnoredFiles = new HashSet<string>(IgnoredFiles, StringComparer.OrdinalIgnoreCase);
        allIgnoredFiles.UnionWith(additionalIgnoredFiles);

        sb.AppendLine("Project Structure:");
        await GenerateDirectoryLayoutAsync(solutionPath, solutionPath, "", sb, allIgnoredDirectories, allIgnoredFiles,
            gitignoreRegexes
        );

        await File.WriteAllTextAsync(outputFile, sb.ToString());
        Console.WriteLine($"Successfully generated directory layout to {outputFile}");
    }

    private static async Task GenerateDirectoryLayoutAsync(string basePath,
        string currentPath,
        string indent,
        StringBuilder sb,
        HashSet<string> ignoredDirectories,
        HashSet<string> ignoredFiles,
        List<Regex> gitignoreRegexes)
    {
        var directories = Directory.GetDirectories(currentPath);
        var files = Directory.GetFiles(currentPath);

        foreach ( var dir in directories.OrderBy(d => d) )
        {
            var dirName = Path.GetFileName(dir);
            var relativePath = Path.GetRelativePath(basePath, dir).Replace('\\', '/');

            if ( ignoredDirectories.Contains(dirName) ||
                 IsIgnoredByGitignore(relativePath, gitignoreRegexes) )
                continue;

            sb.AppendLine($"{indent}📁 {dirName}/");
            await GenerateDirectoryLayoutAsync(
                basePath, dir, indent + "  ", sb,
                ignoredDirectories, ignoredFiles, gitignoreRegexes
            );
        }

        foreach ( var file in files.OrderBy(f => f) )
        {
            var fileName = Path.GetFileName(file);
            var fileExtension = Path.GetExtension(file);
            var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');

            if ( ignoredFiles.Contains(fileName) ||
                 ignoredFiles.Any(ignored => ignored.StartsWith("*.") && fileName.EndsWith(ignored[1..])) ||
                 BinaryExtensions.Contains(fileExtension) ||
                 IsIgnoredByGitignore(relativePath, gitignoreRegexes) )
                continue;

            var fileIcon = GetFileIcon(fileExtension);
            sb.AppendLine($"{indent}{fileIcon} {fileName}");
        }
    }

    private static List<string> LoadGitignoreRules(string directory)
    {
        var gitignorePath = Path.Combine(directory, ".gitignore");
        if ( !File.Exists(gitignorePath) )
            return [];

        return File.ReadAllLines(gitignorePath)
                   .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                   .ToList();
    }

    private static List<Regex> CompileGitignoreRules(List<string> gitignoreRules)
    {
        return gitignoreRules
               .Select(rule =>
                   {
                       var pattern = rule.StartsWith('!') ? rule[1..] : rule;
                       var regexPattern = ConvertGitignorePatternToRegex(pattern);
                       return new Regex(regexPattern, RegexOptions.Compiled);
                   }
               )
               .ToList();
    }

    private static bool IsIgnoredByGitignore(string relativePath, List<Regex> gitignoreRegexes)
    {
        return gitignoreRegexes.Any(regex => regex.IsMatch(relativePath));
    }

    private static string ConvertGitignorePatternToRegex(string pattern)
    {
        var stringBuilder = new StringBuilder();
        var isEscaping = false;

        foreach ( var c in pattern )
            if ( c is '*' or '/' )
            {
                if ( isEscaping )
                {
                    stringBuilder.Length--;
                    isEscaping = false;
                }

                stringBuilder.Append(c);
            }
            else if ( Regex.Escape(c.ToString()).Length > 1 )
            {
                stringBuilder.Append('\\').Append(c);
                isEscaping = true;
            }
            else
            {
                stringBuilder.Append(c);
                isEscaping = false;
            }

        stringBuilder.Replace("**", ".*", 0, stringBuilder.Length);
        stringBuilder.Replace("*", "[^/]*", 0, stringBuilder.Length);

        if ( pattern.EndsWith('/') )
            stringBuilder.Append(".*");
        if ( pattern.StartsWith('/') )
        {
            stringBuilder.Insert(0, '^');
        }
        else
        {
            stringBuilder.Insert(0, "^(.*/)?(");
            stringBuilder.Append(')');
        }

        return stringBuilder.ToString();
    }

    private static string GetFileIcon(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "📄 [C#]",
            ".h" => "📄 [C-Header]",
            ".cpp" => "📄 [C-Source]",
            ".csproj" => "🔧 [Project]",
            ".sln" => "🏗️ [Solution]",
            ".json" => "📋 [JSON]",
            ".xml" => "📋 [XML]",
            ".config" => "⚙️ [Config]",
            ".md" => "📝 [Markdown]",
            ".txt" => "📄 [Text]",
            ".yml" or ".yaml" => "⚙️ [YAML]",
            ".js" or ".jsx" => "📄 [JS]",
            ".ts" or ".tsx" => "📄 [TS]",
            ".css" => "🎨 [CSS]",
            ".scss" => "🎨 [SCSS]",
            ".html" => "🌐 [HTML]",
            _ => "📄"
        };
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: DirectoryLayout <solution-path> <output-file> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --ignore-dir <directory>    Add an additional directory to ignore");
        Console.WriteLine("  --ignore-file <file>        Add an additional file to ignore");
        Console.WriteLine("  --help                      Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  DirectoryLayout ./MyProject ./layout.txt --ignore-dir custom-bin --ignore-file *.tmp");
    }
}