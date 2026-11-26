
using FileSyncX.EfCore.Models;

namespace FileSyncX.Services;

public sealed record HashDefaultSearchResult(
    HashDefault Category,
    double CosineDistance,
    double CosineSimilarity);

public interface IVecktorVaultService
{
    Task<HashDefault> CreateCategoryAsync(
        string description,
        string destinationFolder,
        float[] embedding,
        CancellationToken cancellationToken = default);

    Task<ArchiveHashCategorization> RegisterDocumentAsync(
        string sourceFile,
        string? sha256,
        float[]? embedding = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HashDefaultSearchResult>>
        SearchCategoriesAsync(
            float[] embedding,
            int limit = 5,
            CancellationToken cancellationToken = default);
}
