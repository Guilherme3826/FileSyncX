
using Pgvector;

namespace FileSyncX.EfCore.Models;

public class HashDefault
{
    // Identificador da categoria
    public int Id { get; set; }

    // Descrição da categoria
    public string Description { get; set; } = string.Empty;

    // Pasta de destino
    public string DestinationFolder { get; set; } = string.Empty;

    // Embedding de referência (1536 dimensões)
    public Vector Embedding { get; set; } = null!;

    // Data de criação
    public DateTimeOffset CreatedAt { get; set; }

    // =========================================================
    // RELACIONAMENTO
    // =========================================================

    public ICollection<ArchiveHashCategorization> Documents
    {
        get;
        set;
    } = new List<ArchiveHashCategorization>();
}
