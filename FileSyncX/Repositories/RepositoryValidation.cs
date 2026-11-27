
using Pgvector;

using FileSyncX.EfCore.Models;

namespace FileSyncX.Repositories;

internal static class RepositoryValidation
{
    private const int EmbeddingDimensions = 1536;

    // =========================================================
    // VALIDAR EMBEDDING
    // =========================================================

    public static void ValidateEmbedding(Vector? embedding)
    {
        ArgumentNullException.ThrowIfNull(embedding);

        // Converte para array para verificar as dimensões
        var values = embedding.ToArray();

        if (values.Length != EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"O embedding deve possuir exatamente " +
                $"{EmbeddingDimensions} dimensões.",
                nameof(embedding));
        }

        // Verifica valores NaN e Infinity
        if (values.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException(
                "O embedding contém valores não finitos.",
                nameof(embedding));
        }
    }

    // =========================================================
    // VALIDAR SHA-256
    // =========================================================

    public static void ValidateSha256(string? sha256)
    {
        if (sha256 is null)
            return;

        if (sha256.Length != 64 ||
            !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "O SHA-256 deve possuir exatamente 64 caracteres hexadecimais.",
                nameof(sha256));
        }
    }

    // =========================================================
    // VALIDAR STATUS DE CLASSIFICAÇÃO
    // =========================================================

    public static void ValidateClassificationStatus(string status)
    {
        if (!ArchiveClassificationStatus.IsValid(status))
        {
            throw new ArgumentException(
                $"Status de classificação inválido: {status}",
                nameof(status));
        }
    }
}
