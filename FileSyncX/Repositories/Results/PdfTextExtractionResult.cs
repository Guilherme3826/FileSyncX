
namespace FileSyncX.Repositories.Results;

public sealed record PdfTextExtractionResult(
    string Text,
    bool UsedOcr,
    int PageCount
);
