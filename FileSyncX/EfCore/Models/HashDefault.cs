using Pgvector;

namespace FileSyncX.EfCore.Models;

public class HashDefault
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string DestinationFolder { get; set; } = string.Empty;

    public Vector Embedding { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    // FK para DefaultFoldersClassification
    public long? IdDefaultFolderClassification { get; set; }

    // Navegação para a pasta de classificação
    public DefaultFoldersClassification? DefaultFolderClassification { get; set; }

    // Documentos associados à categoria
    public ICollection<ArchiveHashCategorization> Documents { get; set; }
        = new List<ArchiveHashCategorization>();
}

