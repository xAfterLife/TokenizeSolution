namespace TokenizeSolution_V2_LLM;

public readonly record struct ProjectStructure
(
    string Name,
    string Type,
    string[] Dependencies,
    Dictionary<string, string[]> DirectoryStructure
);