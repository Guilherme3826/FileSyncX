
using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface IPdfTextExtractionService
{
    Task<PdfTextExtractionResult> ExtrairTextoAsync(
        string caminhoPdf,
        CancellationToken cancellationToken = default);
}
