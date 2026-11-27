
using FileSyncX.EfCore.Models;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface IVecktorVaultService
{
    Task<int> CriarOuObterCategoriaAsync(
        string description,
        string destinationFolder,
        CancellationToken cancellationToken = default);

    Task<long> IndexarDocumentoAsync(
        string sourceFile,
        string textoExtraido,
        string? sha256 = null,
        CancellationToken cancellationToken = default);

    Task<long?> BuscarDocumentoPorSha256Async(
        string sha256,
        CancellationToken cancellationToken = default);

    Task<ArchiveHashCategorization?> ObterDocumentoAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task RegistrarDestinoDocumentoAsync(
        long documentId,
        string destinationFile,
        string destinationFolder,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HashDefaultSimilarityResult>>
        BuscarCategoriasSemelhantesAsync(
            string texto,
            int quantidade = 5,
            CancellationToken cancellationToken = default);

    Task<HashDefaultSimilarityResult?> ClassificarDocumentoAsync(
        long documentId,
        double similaridadeMinima,
        CancellationToken cancellationToken = default);
}
