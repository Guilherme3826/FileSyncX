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
    /// inclusive os já classificados e os que estão em revisão.
    /// </summary>
    Task<int> ReclassifyAllAsync(
        double minimumSimilarity = 0.75,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Associa um documento a uma categoria escolhida pelo fluxo automático.
    /// Atualiza também a pasta de destino da categoria e persiste a similaridade
    /// real entre os embeddings do documento e da categoria.
    /// </summary>
    Task<HashDefaultSimilarityResult> AssociateDocumentToCategoryAsync(
        long documentId,
        int categoryId,
        string destinationFolder,
        string? destinationFile,
        CancellationToken cancellationToken = default);
}
