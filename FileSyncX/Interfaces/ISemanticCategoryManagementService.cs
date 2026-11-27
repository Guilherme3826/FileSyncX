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
    /// Reavalia documentos que possuem embedding, mas não estão associados
    /// a uma categoria. Não gera OCR nem novos embeddings dos documentos.
    /// </summary>
    Task<int> ReclassifyWithoutCategoryAsync(
        double minimumSimilarity = 0.75,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Associa um documento já indexado à categoria escolhida pelo fluxo
    /// automático, atualizando os destinos e calculando a similaridade real
    /// entre os embeddings persistidos, sem gerar novos embeddings.
    /// </summary>
    Task<HashDefaultSimilarityResult> AssociateDocumentToCategoryAsync(
        long documentId,
        int categoryId,
        string destinationFolder,
        string? destinationFile,
        CancellationToken cancellationToken = default);
}
