using Pgvector;

using FileSyncX.EfCore.Models;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface IHashDefaultRepository
{
    // =========================================================
    // CONSULTAS
    // =========================================================

    Task<HashDefault?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<HashDefault?> GetByIdWithDocumentsAsync(
        int id,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HashDefault>> GetAllAsync(
        CancellationToken cancellationToken = default);

    // =========================================================
    // PERSISTÊNCIA
    // =========================================================

    Task<HashDefault> AddAsync(
        HashDefault category,
        CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(
        HashDefault category,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);

    // =========================================================
    // PESQUISA VETORIAL
    // =========================================================

    Task<IReadOnlyList<VectorSearchResult<HashDefault>>>
        SearchSimilarAsync(
            Vector embedding,
            int limit = 5,
            CancellationToken cancellationToken = default);
}