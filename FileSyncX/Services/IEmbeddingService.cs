
using System.Threading;
using System.Threading.Tasks;

namespace FileSyncX.Services;

/// <summary>
/// Define operações para geração de embeddings de textos.
/// Independente do provedor de IA utilizado.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Gera um vetor semântico a partir de um texto.
    /// </summary>
    Task<float[]> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);
}
