using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface ICategoryFolderConsolidationService
{
    Task<CategoryFolderSearchResult> BuscarCandidatasAsync(
        CancellationToken cancellationToken = default);

    Task<CategoryFolderConsolidationResult> ConsolidarAsync(
        IReadOnlyList<CategoryFolderMergeCandidate> candidatas,
        CancellationToken cancellationToken = default);
}
