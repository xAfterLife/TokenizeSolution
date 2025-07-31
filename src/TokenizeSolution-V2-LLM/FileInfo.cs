namespace TokenizeSolution_V2_LLM;

public readonly record struct FileInfo
(
    string Path,
    string Extension,
    string Content,
    FileCategory Category,
    int TokenEstimate
);