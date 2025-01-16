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

    private static async Task CompactSolutionAsync(string solutionPath, string outputFile,
        HashSet<string> additionalIgnoredDirectories, HashSet<string> additionalIgnoredFiles)
    {
        var sb = new StringBuilder();
        var channel = Channel.CreateUnbounded<string>();

        var gitignoreRules = LoadGitignoreRules(solutionPath);
        var gitignoreRegexes = CompileGitignoreRules(gitignoreRules);

        var allIgnoredDirectories = new HashSet<string>(IgnoredDirectories, StringComparer.OrdinalIgnoreCase);
        allIgnoredDirectories.UnionWith(additionalIgnoredDirectories);

        var allIgnoredFiles = new HashSet<string>(IgnoredFiles, StringComparer.OrdinalIgnoreCase);
        allIgnoredFiles.UnionWith(additionalIgnoredFiles);

        // Start file discovery
        var discoveryTask = Task.Run(() =>
            DiscoverFiles(solutionPath, channel, allIgnoredDirectories, allIgnoredFiles, gitignoreRegexes)
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

    private static int DiscoverFiles(string directory, Channel<string> channel,
        HashSet<string> ignoredDirectories, HashSet<string> ignoredFiles, List<Regex> gitignoreRegexes)
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
                        file.Contains($"{Path.DirectorySeparatorChar}{ignored}{Path.DirectorySeparatorChar}")
                    );

                // Exclude ignored files
                var isIgnoredFile = ignoredFiles.Contains(fileName) ||
                                    ignoredFiles.Any(ignored =>
                                        ignored.StartsWith("*.") && fileName.EndsWith(ignored[1..])
                                    );

                // Exclude binary file types
                var isBinaryFile = BinaryExtensions.Contains(fileExtension);

                // Exclude based on .gitignore rules
                var isIgnoredByGitignore = IsIgnoredByGitignore(relativePath, gitignoreRegexes);

                if ( !isInIgnoredDirectory && !isIgnoredFile && !isBinaryFile && !isIgnoredByGitignore )
                    channel.Writer.TryWrite(file);
            }
        );

        return totalFiles;
    }

    private static async Task ProcessFilesAsync(Channel<string> channel, StringBuilder sb)
    {
        await foreach ( var file in channel.Reader.ReadAllAsync() )
        {
            var directory = Path.GetDirectoryName(file);
            if ( directory == null )
                continue;
            var relativePath = Path.GetRelativePath(directory, file);
            var content = CompactContent(file);
            var outputLine = $"### {relativePath}\n{content}\n\n";

            lock ( sb )
            {
                sb.Append(outputLine);
                Console.WriteLine($"Processed {relativePath}");
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
            ".ts" or ".js" or ".jsx" or ".tsx" => RemoveCStyleComments(content),
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
        Console.WriteLine("Example:");
        Console.WriteLine("  TokenizeSolution ./MyProject ./output.txt --ignore-dir custom-bin --ignore-file *.tmp");
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
}