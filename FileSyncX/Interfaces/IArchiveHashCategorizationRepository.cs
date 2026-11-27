using Pgvector;

using FileSyncX.EfCore.Models;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface IArchiveHashCategorizationRepository
{
    // =========================================================
    // CONSULTAS
    // =========================================================

    Task<ArchiveHashCategorization?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<ArchiveHashCategorization?> GetByDocumentUuidAsync(
        Guid documentUuid,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveHashCategorization>> GetBySha256Async(
        string sha256,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveHashCategorization>> GetByCategoryIdAsync(
        int categoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArchiveHashCategorization>> GetPendingAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    // =========================================================
    // PERSISTÊNCIA
    // =========================================================

    Task<ArchiveHashCategorization> AddAsync(
        ArchiveHashCategorization document,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        ArchiveHashCategorization document,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateClassificationAsync(
        Guid documentUuid,
        int? categoryId,
        string classificationStatus,
        double? similarityScore,
        string? destinationFile,
        string? destinationFolder,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default);

    // =========================================================
    // PESQUISA VETORIAL
    // =========================================================

    Task<IReadOnlyList<VectorSearchResult<ArchiveHashCategorization>>>
        SearchSimilarAsync(
            Vector embedding,
            int limit = 10,
            CancellationToken cancellationToken = default);
}