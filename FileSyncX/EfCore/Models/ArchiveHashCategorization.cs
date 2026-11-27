
using Pgvector;

namespace FileSyncX.EfCore.Models;

public class ArchiveHashCategorization
{
    // Identificador do registro
    public long Id { get; set; }


    // Identificador único do documento
    public Guid DocumentUuid { get; set; }

    // Hash SHA-256
    public string? Sha256 { get; set; }

    // Caminho do documento original
    public string SourceFile { get; set; } = string.Empty;

    // Embedding semântico do documento
    public Vector? Embedding { get; set; }

    // Arquivo de destino
    public string? DestinationFile { get; set; }

    // Pasta de destino
    public string? DestinationFolder { get; set; }

    // Categoria relacionada
    public int? IdHashDefault { get; set; }

    // Estado da classificação
    public string ClassificationStatus { get; set; } = "pending";

    // Distância ou pontuação de similaridade armazenada
    public double? SimilarityScore { get; set; }

    // Data de criação
    public DateTimeOffset CreatedAt { get; set; }

    // Data da classificação
    public DateTimeOffset? ClassifiedAt { get; set; }

    // =========================================================
    // RELACIONAMENTO
    // =========================================================

    public HashDefault? HashDefault { get; set; }
}
