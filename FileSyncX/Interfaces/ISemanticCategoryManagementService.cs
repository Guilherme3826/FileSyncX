using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface ISemanticCategoryManagementService
{
    Task<IReadOnlyList<SemanticCategoryListItem>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<int> SaveAsync(
        int? categoryId,
        string categoryName,
        string semanticDescription,
        string keywords,
        string destinationFolder,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        int categoryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reavalia todos os documentos que possuem embedding persistido,
    /// inclusive documentos classificados anteriormente ou em revisão.
    /// Não executa OCR nem gera novamente os embeddings dos documentos.
    /// </summary>
    Task<int> ReclassifyAllAsync(
        double minimumSimilarity = 0.75,
        CancellationToken cancellationToken = default);
}
