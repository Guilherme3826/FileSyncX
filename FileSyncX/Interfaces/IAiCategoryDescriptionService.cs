using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface IAiCategoryDescriptionService
{
    Task<AiCategoryDraft> GenerateFromOcrAsync(
        string fileName,
        string ocrText,
        CancellationToken cancellationToken = default);
}
