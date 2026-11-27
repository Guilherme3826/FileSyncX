namespace FileSyncX.Repositories.Results;

public sealed record PdfBatchFileResult(
    string SourceFile,
    bool Succeeded,
    bool AlreadyIndexed,
    string? ClassificationStatus,
    double? Similarity,
    string? Category,
    string? SuggestedDestinationFolder,
    bool Moved,
    string? DestinationFile,
    string? ErrorMessage);

public sealed record PdfBatchFolderWarning(
    string FolderPath,
    string Message);

public sealed record PdfBatchProgress(
    int TotalFiles,
    int ProcessedFiles,
    string? CurrentFile,
    string Message);

public sealed record PdfBatchProcessingResult(
    string SourceFolder,
    int TotalFound,
    IReadOnlyList<PdfBatchFileResult> Files,
    IReadOnlyList<PdfBatchFolderWarning> Warnings,
    bool WasCancelled)
{
    public int Processed => Files.Count;

    public int Succeeded =>
        Files.Count(x => x.Succeeded);

    public int Failed =>
        Files.Count(x => !x.Succeeded);

    public int AlreadyIndexed =>
        Files.Count(x => x.AlreadyIndexed);

    public int Classified =>
        Files.Count(x =>
            string.Equals(
                x.ClassificationStatus,
                "classified",
                StringComparison.OrdinalIgnoreCase));

    public int NeedsReview =>
        Files.Count(x =>
            string.Equals(
                x.ClassificationStatus,
                "review",
                StringComparison.OrdinalIgnoreCase));

    public int Unclassified =>
        Files.Count(x =>
            string.Equals(
                x.ClassificationStatus,
                "unclassified",
                StringComparison.OrdinalIgnoreCase));

    public int Moved =>
        Files.Count(x => x.Moved);
}