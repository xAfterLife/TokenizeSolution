using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace TokenizeSolution_V2_LLM;

public static partial class Program
{
    private const string SectionDelimiter = "\n=== {0} ===\n";

    private static readonly Dictionary<string, FileCategory> ExtensionCategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = FileCategory.Source,
        [".razor"] = FileCategory.Markup,
        [".cshtml"] = FileCategory.Markup,
        [".html"] = FileCategory.Markup,
        [".htm"] = FileCategory.Markup,
        [".css"] = FileCategory.Style,
        [".scss"] = FileCategory.Style,
        [".less"] = FileCategory.Style,
        [".js"] = FileCategory.Script,
        [".ts"] = FileCategory.Script,
        [".jsx"] = FileCategory.Script,
        [".tsx"] = FileCategory.Script,
        [".json"] = FileCategory.Configuration,
        [".xml"] = FileCategory.Configuration,
        [".yaml"] = FileCategory.Configuration,
        [".yml"] = FileCategory.Configuration,
        [".csproj"] = FileCategory.Configuration,
        [".sln"] = FileCategory.Configuration,
        [".props"] = FileCategory.Configuration,
        [".targets"] = FileCategory.Configuration,
        [".md"] = FileCategory.Documentation,
        [".txt"] = FileCategory.Documentation,
        [".rst"] = FileCategory.Documentation,
        [".sql"] = FileCategory.Data
    };

    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".vs", ".idea", ".vscode", ".vsconfig", ".vspscc", ".suo", ".user",
        "packages", "node_modules", "bower_components", "jspm_packages", "typings",
        ".git", ".svn", ".hg", ".env", ".env.local", ".env.development", ".env.production", ".env.test",
        "temp", "tmp", "cache", ".cache", "logs", "*.log", ".DS_Store", "Thumbs.db", "desktop.ini",
        "*.userprefs", "*.sln.cache", "*.suo", "*.lock"
    };

    private static readonly HashSet<string> BlazorIgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "wwwroot/lib", "wwwroot/_framework", "wwwroot/_content", "dist", "build", "out", "publish",
        "sass-cache", ".sass-cache", "css", "js/lib", "js/libs", "js/vendor", "packages", "package",
        "nuget", ".nuget", "clientbin", "generatedassets", "_bin_deployableassemblies",
        "launchsettings", ".launchsettings"
    };

    private static readonly HashSet<string> IgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gitignore", ".gitattributes", ".gitmodules", ".gitkeep", ".npmrc", ".yarnrc",
        ".editorconfig", ".eslintrc", ".prettierrc", "package-lock.json", "yarn.lock",
        "pnpm-lock.yaml", "*.tmp", "*.bak", "*.swp", "*.swo", "*.log", "*.pid", "*.seed", "*.pid.lock"
    };

    private static readonly HashSet<string> BlazorIgnoredFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "*.min.css", "*.min.js", "*.bundle.js", "*.bundle.css", "blazor.boot.json",
        "blazor.webassembly.js", "dotnet.js", "dotnet.wasm", "*.nupkg", "*.snupkg",
        "*.symbols.nupkg", "*.nuspec", "packages.config", "packages.lock.json",
        "*.compiled.css", "*.generated.css", "*.scss.css", "*.less.css",
        "webpack.config.js", "rollup.config.js", "vite.config.js", "tsconfig.json",
        "jsconfig.json", "launchSettings.json", "*.pubxml", "*.pubxml.user",
        "*.dll.config", "*.exe.config", "*.runtimeconfig.json", "*.deps.json",
        "*.pdb", "*.xml", "*.resources"
    };

    private static readonly HashSet<string?> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp",
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv",
        ".zip", ".rar", ".tar", ".gz", ".7z", ".exe", ".dll", ".pdb", ".so", ".dylib", ".lib",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf",
        ".ttf", ".otf", ".woff", ".woff2", ".eot", ".res", ".resx", ".wasm", ".blat", ".dat", ".br"
    };

    private static readonly HashSet<string?> BlazorRelevantExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".razor", ".cshtml", ".csproj", ".sln", ".props", ".targets",
        ".json", ".xml", ".yaml", ".yml", ".css", ".js", ".html", ".htm", ".md", ".txt", ".rst"
    };

    private static readonly Regex BlazorMinifiedFileRegex = MinifiedFileRegex();
    private static readonly Regex BlazorGeneratedFileRegex = GeneratedFileRegex();

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
        var outputFormat = OutputFormat.Hierarchical;
        var includeMetadata = true;
        var maxTokens = 150000; // Conservative estimate for most LLMs
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
                case "--format" when i + 1 < args.Length:
                    if ( Enum.TryParse<OutputFormat>(args[i + 1], true, out var format) )
                        outputFormat = format;
                    i++;
                    break;
                case "--no-metadata":
                    includeMetadata = false;
                    break;
                case "--max-tokens" when i + 1 < args.Length:
                    if ( int.TryParse(args[i + 1], out var tokens) )
                        maxTokens = tokens;
                    i++;
                    break;
            }

        try
        {
            var startTime = DateTime.UtcNow;
            await CompactSolutionAsync(solutionPath, outputFile, outputFormat, includeMetadata, maxTokens,
                additionalIgnoredDirectories, additionalIgnoredFiles
            );
            var endTime = DateTime.UtcNow;
            Console.WriteLine($"Elapsed time: {(endTime - startTime).TotalMilliseconds:F2} ms");
        }
        catch ( Exception ex )
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            await Task.Delay(1500);
        }
    }

    private static async Task CompactSolutionAsync(string solutionPath, string outputFile,
        OutputFormat format, bool includeMetadata, int maxTokens,
        HashSet<string> additionalIgnoredDirectories, HashSet<string> additionalIgnoredFiles)
    {
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

        var fileInfos = new List<FileInfo>();
        var discoveryTask = Task.Run(() => DiscoverFiles(solutionPath, channel,
                allIgnoredDirectories, allIgnoredFiles, gitignoreRegexes, isBlazorProject
            )
        );

        var processingTasks = Enumerable.Range(0, Environment.ProcessorCount)
                                        .Select(_ => ProcessFilesAsync(channel, fileInfos, solutionPath))
                                        .ToArray();

        await discoveryTask;
        channel.Writer.Complete();
        await Task.WhenAll(processingTasks);

        // Sort files by importance for LLMs
        var prioritizedFiles = PrioritizeFilesForLlm(fileInfos);
        var trimmedFiles = TrimToTokenLimit(prioritizedFiles, maxTokens);

        var projectStructure = includeMetadata ? AnalyzeProjectStructure(solutionPath) : null;

        await WriteOptimizedOutputAsync(outputFile, trimmedFiles, projectStructure, format, isBlazorProject);

        Console.WriteLine($"Successfully compacted solution to {outputFile}");
        Console.WriteLine($"Processed {trimmedFiles.Count} files ({fileInfos.Count - trimmedFiles.Count} trimmed)");
        Console.WriteLine($"Estimated tokens: {trimmedFiles.Sum(f => f.TokenEstimate):N0}");
    }

    private static async Task ProcessFilesAsync(Channel<string> channel, List<FileInfo> fileInfos, string basePath)
    {
        await foreach ( var file in channel.Reader.ReadAllAsync() )
            try
            {
                var relativePath = Path.GetRelativePath(basePath, file).Replace('\\', '/');
                var extension = Path.GetExtension(file);
                var content = CompactContent(file);

                if ( string.IsNullOrWhiteSpace(content) )
                    continue;

                var category = ExtensionCategoryMap.GetValueOrDefault(extension, FileCategory.Source);
                var tokenEstimate = EstimateTokens(content);

                var fileInfo = new FileInfo(relativePath, extension, content, category, tokenEstimate);

                lock ( fileInfos )
                {
                    fileInfos.Add(fileInfo);
                }
            }
            catch ( Exception ex )
            {
                Console.WriteLine($"Warning: Failed to process file {file}: {ex.Message}");
            }
    }

    private static List<FileInfo> PrioritizeFilesForLlm(List<FileInfo> files)
    {
        // Priority order for LLM understanding
        var categoryPriority = new Dictionary<FileCategory, int>
        {
            [FileCategory.Configuration] = 1, // Project structure first
            [FileCategory.Source] = 2, // Core logic
            [FileCategory.Markup] = 3, // UI structure
            [FileCategory.Style] = 4, // Styling
            [FileCategory.Script] = 5, // Client logic
            [FileCategory.Documentation] = 6, // Context
            [FileCategory.Data] = 7 // Data files last
        };

        return files
               .OrderBy(f => categoryPriority.GetValueOrDefault(f.Category, 999))
               .ThenBy(f => f.Path.Count(c => c == '/')) // Depth - root files first
               .ThenByDescending(f => GetFileImportance(f.Path)) // Important files first
               .ThenBy(f => f.Path)
               .ToList();
    }

    private static int GetFileImportance(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        var importantNames = new[] { "program", "startup", "app", "main", "index", "layout", "appsettings" };

        if ( importantNames.Any(name => fileName.Contains(name)) )
            return 100;
        if ( path.Contains("controller", StringComparison.OrdinalIgnoreCase) )
            return 90;
        if ( path.Contains("service", StringComparison.OrdinalIgnoreCase) )
            return 80;
        if ( path.Contains("model", StringComparison.OrdinalIgnoreCase) )
            return 70;
        return path.Contains("component", StringComparison.OrdinalIgnoreCase) ? 60 : 0;
    }

    private static List<FileInfo> TrimToTokenLimit(List<FileInfo> files, int maxTokens)
    {
        var result = new List<FileInfo>();
        var currentTokens = 0;

        foreach ( var file in files.TakeWhile(file => currentTokens + file.TokenEstimate <= maxTokens || result.Count <= 0) )
        {
            result.Add(file);
            currentTokens += file.TokenEstimate;
        }

        return result;
    }

    private static int EstimateTokens(string content)
    {
        // Rough approximation: 1 token ≈ 4 characters for code
        return content.Length / 4;
    }

    private static ProjectStructure? AnalyzeProjectStructure(string solutionPath)
    {
        try
        {
            var projectFiles = Directory.GetFiles(solutionPath, "*.csproj", SearchOption.TopDirectoryOnly);
            if ( projectFiles.Length == 0 )
                return null;

            var projectFile = projectFiles[0];
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var content = File.ReadAllText(projectFile);

            var dependencies = ExtractPackageReferences(content);
            var projectType = DetermineProjectType(content);
            var directoryStructure = BuildDirectoryMap(solutionPath);

            return new ProjectStructure(projectName, projectType, dependencies, directoryStructure);
        }
        catch
        {
            return null;
        }
    }

    private static string[] ExtractPackageReferences(string projectContent)
    {
        var matches = Regex.Matches(projectContent, @"<PackageReference\s+Include=""([^""]+)""");
        return matches.Select(m => m.Groups[1].Value).ToArray();
    }

    private static string DetermineProjectType(string projectContent)
    {
        if ( projectContent.Contains("Microsoft.AspNetCore.Components.WebAssembly") )
            return "Blazor WebAssembly";
        if ( projectContent.Contains("Microsoft.AspNetCore.Components") )
            return "Blazor Server";
        if ( projectContent.Contains("Microsoft.AspNetCore") )
            return "ASP.NET Core";
        if ( projectContent.Contains("Microsoft.WindowsDesktop.App") )
            return "WPF/WinForms";
        return projectContent.Contains("Microsoft.NET.Sdk.Web") ? "Web Application" : "Console/Library";
    }

    private static Dictionary<string, string[]> BuildDirectoryMap(string basePath)
    {
        var result = new Dictionary<string, string[]>();

        try
        {
            var directories = Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories)
                                       .Where(d => !IgnoredDirectories.Any(ignored =>
                                               d.Contains($"{Path.DirectorySeparatorChar}{ignored}{Path.DirectorySeparatorChar}")
                                           )
                                       )
                                       .Take(50); // Limit to prevent excessive output

            foreach ( var dir in directories )
            {
                var relativePath = Path.GetRelativePath(basePath, dir).Replace('\\', '/');
                var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                                     .Select(Path.GetFileName)
                                     .Where(f => !BinaryExtensions.Contains(Path.GetExtension(f)))
                                     .Take(20) // Limit files per directory
                                     .ToArray();

                if ( files.Length > 0 )
                    result[relativePath] = files!;
            }
        }
        catch { }

        return result;
    }

    [RequiresUnreferencedCode("Calls TokenizeSolution_V2_LLM.Program.WriteStructuredFormat(StreamWriter, List<FileInfo>)")]
    [RequiresDynamicCode("Calls TokenizeSolution_V2_LLM.Program.WriteStructuredFormat(StreamWriter, List<FileInfo>)")]
    private static async Task WriteOptimizedOutputAsync(string outputFile, List<FileInfo> files,
        ProjectStructure? projectStructure, OutputFormat format, bool isBlazorProject)
    {
        await using var writer = new StreamWriter(outputFile, false, Encoding.UTF8);

        // Write LLM-optimized header
        await writer.WriteLineAsync("# PROJECT ANALYSIS");
        await writer.WriteLineAsync($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        await writer.WriteLineAsync($"Files: {files.Count}");
        await writer.WriteLineAsync($"Estimated Tokens: {files.Sum(f => f.TokenEstimate):N0}");

        if ( isBlazorProject )
            await writer.WriteLineAsync("Project Type: Blazor Application");

        if ( projectStructure.HasValue )
        {
            await writer.WriteLineAsync("\n## PROJECT STRUCTURE");
            await writer.WriteLineAsync($"Name: {projectStructure.Value.Name}");
            await writer.WriteLineAsync($"Type: {projectStructure.Value.Type}");

            if ( projectStructure.Value.Dependencies.Length > 0 )
                await writer.WriteLineAsync($"Dependencies: {string.Join(", ", projectStructure.Value.Dependencies)}");

            if ( projectStructure.Value.DirectoryStructure.Count > 0 )
            {
                await writer.WriteLineAsync("\n### Directory Structure:");
                foreach ( var (dir, dirFiles) in projectStructure.Value.DirectoryStructure.Take(10) )
                    await writer.WriteLineAsync($"- {dir}/ ({dirFiles.Length} files)");
            }
        }

        await writer.WriteLineAsync("\n## FILE CONTENTS");

        switch ( format )
        {
            case OutputFormat.Hierarchical:
                await WriteHierarchicalFormat(writer, files);
                break;
            case OutputFormat.Structured:
                await WriteStructuredFormat(writer, files);
                break;
            default:
                await WriteFlatFormat(writer, files);
                break;
        }
    }

    private static async Task WriteHierarchicalFormat(StreamWriter writer, List<FileInfo> files)
    {
        var filesByCategory = files.GroupBy(f => f.Category).OrderBy(g => g.Key);

        foreach ( var category in filesByCategory )
        {
            await writer.WriteLineAsync(string.Format(SectionDelimiter, $"{category.Key.ToString().ToUpperInvariant()} FILES"));

            foreach ( var file in category )
            {
                await writer.WriteLineAsync(file.Content);
            }
        }
    }

    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
    [RequiresDynamicCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
    private static async Task WriteStructuredFormat(StreamWriter writer, List<FileInfo> files)
    {
        var structure = new
        {
            files = files.Select(f => new
                             {
                                 path = f.Path,
                                 category = f.Category.ToString(),
                                 tokens = f.TokenEstimate,
                                 content = f.Content
                             }
                         )
                         .ToArray()
        };

        var json = JsonSerializer.Serialize(structure, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            }
        );

        await writer.WriteAsync(json);
    }

    private static async Task WriteFlatFormat(StreamWriter writer, List<FileInfo> files)
    {
        foreach ( var file in files )
        {
            await writer.WriteLineAsync(file.Content);
        }
    }

    private static bool DetectBlazorProject(string solutionPath)
    {
        try
        {
            var projectFiles = Directory.GetFiles(solutionPath, "*.csproj", SearchOption.AllDirectories);
            foreach ( var projectFile in projectFiles )
                try
                {
                    var content = File.ReadAllText(projectFile);
                    if ( content.Contains("Microsoft.AspNetCore.Components") ||
                         content.Contains("Microsoft.AspNetCore.Components.WebAssembly") ||
                         content.Contains("Blazor") ||
                         content.Contains("<Project Sdk=\"Microsoft.NET.Sdk.BlazorWebAssembly\">") ||
                         content.Contains("<Project Sdk=\"Microsoft.NET.Sdk.Web\">") )
                        return true;
                }
                catch { }

            var blazorIndicators = new[] { "*.razor" };
            foreach ( var pattern in blazorIndicators )
                try
                {
                    if ( Directory.GetFiles(solutionPath, pattern, SearchOption.AllDirectories).Length > 0 )
                        return true;
                }
                catch { }

            var specificFiles = new[] { "_Imports.razor", "App.razor", "MainLayout.razor" };
            foreach ( var file in specificFiles )
                try
                {
                    if ( Directory.GetFiles(solutionPath, file, SearchOption.AllDirectories).Length > 0 )
                        return true;
                }
                catch { }

            try
            {
                var wwwrootDirs = Directory.GetDirectories(solutionPath, "wwwroot", SearchOption.AllDirectories);
                if ( wwwrootDirs.Length > 0 )
                    if ( wwwrootDirs.Select(wwwrootDir => Path.Combine(wwwrootDir, "index.html")).Any(File.Exists) )
                    {
                        return true;
                    }
            }
            catch { }

            return false;
        }
        catch
        {
            return false;
        }
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

                var isInIgnoredDirectory = ignoredDirectories
                    .Any(ignored => file.Contains($"{Path.DirectorySeparatorChar}{ignored}{Path.DirectorySeparatorChar}") ||
                                    relativePath.StartsWith($"{ignored}/", StringComparison.OrdinalIgnoreCase)
                    );

                var isIgnoredFile = ignoredFiles.Contains(fileName) ||
                                    ignoredFiles.Any(ignored => ignored.StartsWith("*.") &&
                                                                fileName.EndsWith(ignored[1..], StringComparison.OrdinalIgnoreCase)
                                    );

                var isBinaryFile = BinaryExtensions.Contains(fileExtension);
                var isIgnoredByGitignore = IsIgnoredByGitignore(relativePath, gitignoreRegexes);
                var isBlazorExcluded = false;

                if ( isBlazorProject )
                    isBlazorExcluded = IsBlazorSpecificExclusion(relativePath, fileName, fileExtension);

                if ( !isInIgnoredDirectory && !isIgnoredFile && !isBinaryFile && !isIgnoredByGitignore && !isBlazorExcluded )
                    channel.Writer.TryWrite(file);
            }
        );

        return totalFiles;
    }

    private static bool IsBlazorSpecificExclusion(string relativePath, string fileName, string? fileExtension)
    {
        if ( BlazorMinifiedFileRegex.IsMatch(fileName) )
            return true;
        if ( BlazorGeneratedFileRegex.IsMatch(fileName) )
            return true;

        if ( relativePath.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase) )
        {
            var allowedWwwrootFiles = new[] { ".html", ".css", ".js" };
            if ( !allowedWwwrootFiles.Contains(fileExtension, StringComparer.OrdinalIgnoreCase) )
                return true;
            if ( fileName.Contains(".min.", StringComparison.OrdinalIgnoreCase) )
                return true;
        }

        if ( !BlazorRelevantExtensions.Contains(fileExtension) )
            return true;

        var excludePatterns = new[] { "/publish/", "/dist/", "/build/", "/.vs/", "/bin/", "/obj/" };
        return excludePatterns.Any(pattern => relativePath.Contains(pattern, StringComparison.OrdinalIgnoreCase));
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

        content = extension switch
        {
            ".cs" => RemoveCStyleComments(content),
            ".razor" => RemoveRazorComments(content),
            ".ts" or ".js" or ".jsx" or ".tsx" => RemoveCStyleComments(content),
            ".css" => RemoveCssComments(content),
            _ => content
        };

        // Enhanced content processing for LLM optimization
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.Trim())
                           .Where(l => !string.IsNullOrWhiteSpace(l))
                           .ToArray();

        if ( lines.Length == 0 )
            return string.Empty;

        var first = lines.FirstOrDefault();
        var joiner = first?.StartsWith('<') == true || first?.StartsWith('{') == true ? "\n" : " ";

        // For structured files (JSON, XML, HTML), preserve line breaks
        if ( extension is ".json" or ".xml" or ".html" or ".htm" or ".razor" or ".cshtml" )
            joiner = "\n";

        return string.Join(joiner, lines);
    }

    private static string RemoveCStyleComments(string content)
    {
        // Remove multi-line comments
        content = CommentRegex1().Replace(content, "");
        // Remove single-line comments but preserve URLs
        content = CommentRegex2().Replace(content, "");
        // Remove XML documentation comments
        content = CommentRegex3().Replace(content, "");
        // Remove pragma directives
        content = CommentRegex4().Replace(content, "");
        // Remove region directives
        content = CommentRegex5().Replace(content, "");
        // Remove empty lines
        content = CommentRegex6().Replace(content, "");

        return content;
    }

    private static string RemoveRazorComments(string content)
    {
        content = RazorCommentRegex().Replace(content, "");
        return RemoveCStyleComments(content);
    }

    private static string RemoveCssComments(string content)
    {
        return CssCommentRegex().Replace(content, "");
    }

    private static void ShowHelp()
    {
        Console.WriteLine("Enhanced TokenizeSolution - LLM-Optimized Code Compactor");
        Console.WriteLine("Usage: TokenizeSolution <solution-path> <output-file> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --ignore-dir <directory>    Add an additional directory to ignore");
        Console.WriteLine("  --ignore-file <file>        Add an additional file to ignore");
        Console.WriteLine("  --format <format>           Output format: Hierarchical, Flat, Structured (default: Hierarchical)");
        Console.WriteLine("  --no-metadata               Skip project metadata analysis");
        Console.WriteLine("  --max-tokens <number>       Maximum estimated tokens (default: 150000)");
        Console.WriteLine("  --help                      Show this help message");
        Console.WriteLine();
        Console.WriteLine("LLM Optimizations:");
        Console.WriteLine("  - Intelligent file prioritization (config → source → UI → docs)");
        Console.WriteLine("  - Token estimation and automatic trimming");
        Console.WriteLine("  - Project structure analysis and metadata");
        Console.WriteLine("  - Categorized output with clear section delimiters");
        Console.WriteLine("  - Enhanced comment removal and content compaction");
        Console.WriteLine("  - Support for multiple output formats");
        Console.WriteLine();
        Console.WriteLine("Output Formats:");
        Console.WriteLine("  Hierarchical - Files grouped by category with clear sections");
        Console.WriteLine("  Flat         - Simple linear file listing");
        Console.WriteLine("  Structured   - JSON format for programmatic processing");
        Console.WriteLine();
        Console.WriteLine("Features:");
        Console.WriteLine("  - Automatically detects Blazor projects");
        Console.WriteLine("  - Filters out minified, compiled, and generated files");
        Console.WriteLine("  - Respects .gitignore rules");
        Console.WriteLine("  - Removes comments while preserving essential code structure");
        Console.WriteLine("  - Provides token estimates for LLM context planning");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  TokenizeSolution ./MyBlazorProject ./output.txt");
        Console.WriteLine("  TokenizeSolution ./MyProject ./output.json --format Structured --max-tokens 100000");
        Console.WriteLine("  TokenizeSolution ./MyProject ./output.txt --ignore-dir custom-bin --no-metadata");
    }

    // Regex patterns using source generators for AOT compatibility
    [GeneratedRegex(@"/\*[\s\S]*?\*/", RegexOptions.Compiled)]
    private static partial Regex CommentRegex1();

    [GeneratedRegex(@"(?<!:)//(?!//).*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex2();

    [GeneratedRegex(@"^\s*///.*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex3();

    [GeneratedRegex(@"^\s*#pragma\s.*$", RegexOptions.Multiline | RegexOptions.Compiled)]
    private static partial Regex CommentRegex4();

    [GeneratedRegex(@"^\s*#(region|endregion)\s*.*$", RegexOptions.Multiline | RegexOptions.Compiled)]
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