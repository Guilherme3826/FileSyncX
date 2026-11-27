namespace FileSyncX.Repositories.Results;

public sealed record PdfSemanticProcessingResult(
    long DocumentId,
    string SourceFile,
    string Sha256,
    bool AlreadyIndexed,
    int PageCount,
    int ExtractedCharacters,
    bool UsedOcr,
    string ClassificationStatus,
    double? Similarity,
    string? Category,
    string? SuggestedDestinationFolder,
    bool Moved,
    string? DestinationFile);