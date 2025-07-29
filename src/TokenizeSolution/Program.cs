using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace TokenizeSolution;

public static partial class SolutionCompactor
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

    private static readonly HashSet<string> BlazorIgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        // Blazor WebAssembly specific
        "wwwroot/lib", "wwwroot/_framework", "wwwroot/_content",
        // Generated/compiled assets
        "dist", "build", "out", "publish",
        // CSS/JS tooling
        "sass-cache", ".sass-cache", "css", "js/lib", "js/libs", "js/vendor",
        // Package management
        "packages", "package", "nuget", ".nuget",
        // Build artifacts
        "clientbin", "generatedassets", "_bin_deployableassemblies",
        // Development tools
        "launchsettings", ".launchsettings"
    };

    private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Common files to ignore
        ".gitignore", ".gitattributes", ".gitmodules", ".gitkeep",
        ".npmrc", ".yarnrc", ".editorconfig", ".eslintrc", ".prettierrc",
        "package-lock.json", "yarn.lock", "pnpm-lock.yaml",
        "*.tmp", "*.bak", "*.swp", "*.swo", "*.log", "*.pid", "*.seed", "*.pid.lock"
    };

    private static readonly HashSet<string> BlazorIgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Blazor specific generated files
        "*.min.css", "*.min.js", "*.bundle.js", "*.bundle.css",
        "blazor.boot.json", "blazor.webassembly.js", "dotnet.js", "dotnet.wasm",
        // Build and package files
        "*.nupkg", "*.snupkg", "*.symbols.nupkg",
        "*.nuspec", "packages.config", "packages.lock.json",
        // CSS/JS build artifacts
        "*.compiled.css", "*.generated.css", "*.scss.css", "*.less.css",
        "webpack.config.js", "rollup.config.js", "vite.config.js",
        "tsconfig.json", "jsconfig.json",
        // Launch and debug files
        "launchSettings.json", "*.pubxml", "*.pubxml.user",
        // Compiled output
        "*.dll.config", "*.exe.config", "*.runtimeconfig.json",
        "*.deps.json", "*.pdb", "*.xml", "*.resources"
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
        ".res", ".resx", // Resource Files
        // Blazor/Web specific binaries
        ".wasm", ".blat", ".dat", ".br" // WebAssembly and compressed files
    };

    private static readonly HashSet<string> BlazorRelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Source code files
        ".cs", ".razor", ".cshtml",
        // Configuration and project files
        ".csproj", ".sln", ".props", ".targets",
        // Application configuration
        ".json", ".xml", ".yaml", ".yml",
        // Web files (only unminified)
        ".css", ".js", ".html", ".htm",
        // Documentation
        ".md", ".txt", ".rst"
    };

    private static readonly Regex BlazorMinifiedFileRegex = MinifiedFileRegex();
    private static readonly Regex BlazorGeneratedFileRegex = GeneratedFileRegex();

    private static async Task CompactSolutionAsync(string solutionPath, string outputFile,
        HashSet<string> additionalIgnoredDirectories, HashSet<string> additionalIgnoredFiles)
    {
        var sb = new StringBuilder();
        var channel = Channel.CreateUnbounded<string>();

        var isBlazorProject = DetectBlazorProject(solutionPath);
        var gitignoreRules = LoadGitignoreRules(solutionPath);
        var gitignoreRegexes = CompileGitignoreRules(gitignoreRules);

        var allIgnoredDirectories = new HashSet<string>(IgnoredDirectories, StringComparer.OrdinalIgnoreCase);
        var allIgnoredFiles = new HashSet<string>(IgnoredFiles, StringComparer.OrdinalIgnoreCase);

        if ( isBlazorProject )
        {
            Console.WriteLine("Blazor project detected - applying Blazor-specific filtering rules");
            allIgnoredDirectories.UnionWith(BlazorIgnoredDirectories);
            allIgnoredFiles.UnionWith(BlazorIgnoredFiles);
        }

        allIgnoredDirectories.UnionWith(additionalIgnoredDirectories);
        allIgnoredFiles.UnionWith(additionalIgnoredFiles);

        // Start file discovery
        var discoveryTask = Task.Run(() =>
            DiscoverFiles(solutionPath, channel, allIgnoredDirectories, allIgnoredFiles, gitignoreRegexes, isBlazorProject)
        );

        // Start processing files
        var processingTasks = Enumerable.Range(0, Environment.ProcessorCount)
                                        .Select(_ => ProcessFilesAsync(channel, sb))
                                        .ToArray();

        await discoveryTask; // Wait for discovery to finish
        channel.Writer.Complete(); // Signal that no more files will be written

        await Task.WhenAll(processingTasks); // Wait for all processing to finish

        await File.WriteAllTextAsync(outputFile, sb.ToString());
        Console.WriteLine($"Successfully compacted solution to {outputFile}");
    }

    private static bool DetectBlazorProject(string solutionPath)
    {
        // Check for Blazor project indicators
        var projectFiles = Directory.GetFiles(solutionPath, "*.csproj", SearchOption.AllDirectories);

        foreach ( var projectFile in projectFiles )
        {
            try
            {
                var content = File.ReadAllText(projectFile);
                if ( content.Contains("Microsoft.AspNetCore.Components") ||
                     content.Contains("Microsoft.AspNetCore.Components.WebAssembly") ||
                     content.Contains("Blazor") ||
                     content.Contains("<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\">") ||
                     content.Contains("<Project Sdk=\"Microsoft.NET.Sdk.Web\">") )
                {
                    return true;
                }
            }
            catch
            {
                // Ignore file read errors and continue checking
            }
        }

        // Check for common Blazor files
        var blazorIndicators = new[]
        {
            "_Imports.razor",
            "App.razor",
            "MainLayout.razor",
            "wwwroot/index.html"
        };

        return blazorIndicators.Any(indicator =>
            Directory.GetFiles(solutionPath, indicator, SearchOption.AllDirectories).Length > 0
        );
    }

    private static int DiscoverFiles(string directory, Channel<string> channel,
        HashSet<string> ignoredDirectories, HashSet<string> ignoredFiles,
        List<Regex> gitignoreRegexes, bool isBlazorProject)
    {
        var files = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);
        var totalFiles = files.Length;

        Parallel.ForEach(files, file =>
            {
                var relativePath = Path.GetRelativePath(directory, file).Replace('\\', '/');
                var fileName = Path.GetFileName(file);
                var fileExtension = Path.GetExtension(file);

                // Exclude ignored directories
                var isInIgnoredDirectory = ignoredDirectories
                    .Any(ignored =>
                        file.Contains($"{Path.DirectorySeparatorChar}{ignored}{Path.DirectorySeparatorChar}") ||
                        relativePath.StartsWith($"{ignored}/", StringComparison.OrdinalIgnoreCase)
                    );

                // Exclude ignored files
                var isIgnoredFile = ignoredFiles.Contains(fileName) ||
                                    ignoredFiles.Any(ignored =>
                                        ignored.StartsWith("*.") && fileName.EndsWith(ignored[1..], StringComparison.OrdinalIgnoreCase)
                                    );

                // Exclude binary file types
                var isBinaryFile = BinaryExtensions.Contains(fileExtension);

                // Exclude based on .gitignore rules
                var isIgnoredByGitignore = IsIgnoredByGitignore(relativePath, gitignoreRegexes);

                // Blazor-specific filtering
                var isBlazorExcluded = false;
                if ( isBlazorProject )
                {
                    isBlazorExcluded = IsBlazorSpecificExclusion(relativePath, fileName, fileExtension);
                }

                if ( !isInIgnoredDirectory &&
                     !isIgnoredFile &&
                     !isBinaryFile &&
                     !isIgnoredByGitignore &&
                     !isBlazorExcluded )
                    channel.Writer.TryWrite(file);
            }
        );

        return totalFiles;
    }

    private static bool IsBlazorSpecificExclusion(string relativePath, string fileName, string fileExtension)
    {
        // Skip minified files
        if ( BlazorMinifiedFileRegex.IsMatch(fileName) )
            return true;

        // Skip generated files
        if ( BlazorGeneratedFileRegex.IsMatch(fileName) )
            return true;

        // Skip files in wwwroot that are not relevant source files
        if ( relativePath.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase) )
        {
            // Allow only specific files in wwwroot that might be relevant
            var allowedWwwrootFiles = new[] { ".html", ".css", ".js" };
            if ( !allowedWwwrootFiles.Contains(fileExtension, StringComparer.OrdinalIgnoreCase) )
                return true;

            // But still exclude minified versions
            if ( fileName.Contains(".min.", StringComparison.OrdinalIgnoreCase) )
                return true;
        }

        // For Blazor projects, only include files with relevant extensions
        if ( !BlazorRelevantExtensions.Contains(fileExtension) )
            return true;

        // Skip specific patterns
        var excludePatterns = new[]
        {
            "/publish/",
            "/dist/",
            "/build/",
            "/.vs/",
            "/bin/",
            "/obj/"
        };

        return excludePatterns.Any(pattern =>
            relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        );
    }

    private static async Task ProcessFilesAsync(Channel<string> channel, StringBuilder sb)
    {
        await foreach ( var file in channel.Reader.ReadAllAsync() )
        {
            try
            {
                var directory = Path.GetDirectoryName(file);
                if ( directory == null )
                    continue;

                var relativePath = Path.GetRelativePath(directory, file);
                var content = CompactContent(file);

                if ( string.IsNullOrWhiteSpace(content) )
                    continue;

                var outputLine = $"### {relativePath}\n{content}\n\n";

                lock ( sb )
                {
                    sb.Append(outputLine);
                    Console.WriteLine($"Processed {relativePath}");
                }
            }
            catch ( Exception ex )
            {
                Console.WriteLine($"Warning: Failed to process file {file}: {ex.Message}");
            }
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

    private static string CompactContent(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var content = File.ReadAllText(filePath);

        // Remove comments based on file type
        content = extension switch
        {
            ".cs" => RemoveCStyleComments(content),
            ".razor" => RemoveRazorComments(content),
            ".ts" or ".js" or ".jsx" or ".tsx" => RemoveCStyleComments(content),
            ".css" => RemoveCssComments(content),
            _ => content
        };

        var lines = content.Split(["\r\n", "\r", "\n"], StringSplitOptions.None)
                           .Select(line => line.Trim())
                           .Where(line => !string.IsNullOrWhiteSpace(line))
                           .ToList();

        var isXmlOrJson = lines.FirstOrDefault()?.TrimStart().StartsWith('<') == true ||
                          lines.FirstOrDefault()?.TrimStart().StartsWith('{') == true;
        return string.Join(isXmlOrJson ? "" : "\n", lines);
    }

    private static string RemoveCStyleComments(string content)
    {
        content = CommentRegex1().Replace(content, "");
        content = CommentRegex2().Replace(content, "");
        content = CommentRegex3().Replace(content, "");
        content = CommentRegex4().Replace(content, "");
        content = CommentRegex5().Replace(content, "");
        content = CommentRegex6().Replace(content, "");

        return content;
    }

    private static string RemoveRazorComments(string content)
    {
        // Remove Razor comments @* ... *@
        content = RazorCommentRegex().Replace(content, "");
        // Also remove C# style comments within Razor
        return RemoveCStyleComments(content);
    }

    private static string RemoveCssComments(string content)
    {
        // Remove CSS comments /* ... */
        return CssCommentRegex().Replace(content, "");
    }

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

        // Parse additional ignored directories and files from command-line arguments
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
            await CompactSolutionAsync(solutionPath, outputFile, additionalIgnoredDirectories, additionalIgnoredFiles);
            var endTime = DateTime.UtcNow;

            Console.WriteLine($"Elapsed time: {(endTime - startTime).TotalMilliseconds} ms.");
            await Task.Delay(1000);
        }
        catch ( Exception ex )
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Usage: TokenizeSolution <solution-path> <output-file> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --ignore-dir <directory>    Add an additional directory to ignore");
        Console.WriteLine("  --ignore-file <file>        Add an additional file to ignore");
        Console.WriteLine("  --help                      Show this help message");
        Console.WriteLine();
        Console.WriteLine("Features:");
        Console.WriteLine("  - Automatically detects Blazor projects");
        Console.WriteLine("  - Filters out minified, compiled, and generated files");
        Console.WriteLine("  - Respects .gitignore rules");
        Console.WriteLine("  - Removes comments from source files");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  TokenizeSolution ./MyBlazorProject ./output.txt --ignore-dir custom-bin --ignore-file *.tmp");
    }

    [GeneratedRegex(@"/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentRegex1();

    [GeneratedRegex("(?<!:)//(?!/).*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex2();

    [GeneratedRegex("///.*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex3();

    [GeneratedRegex(@"^\s*//\s*$\n?", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex4();

    [GeneratedRegex(@"^\s*#(region|endregion)\s*$\n?", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex5();

    [GeneratedRegex(@"^\s*$\n", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex6();

    [GeneratedRegex(@"@\*[\s\S]*?\*@", RegexOptions.Compiled)]
    private static partial Regex RazorCommentRegex();

    [GeneratedRegex(@"/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CssCommentRegex();

    [GeneratedRegex(@".*\.min\.(js|css|html)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex MinifiedFileRegex();

    [GeneratedRegex(@".*\.(generated|g|designer)\.(cs|js|css)$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex GeneratedFileRegex();
}