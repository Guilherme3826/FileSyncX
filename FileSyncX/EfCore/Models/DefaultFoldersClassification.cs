namespace FileSyncX.EfCore.Models;

public class DefaultFoldersClassification
{
    public long Id { get; set; }

    public string FolderName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ClassifiedAt { get; set; }

    // Categorias associadas a esta pasta
    public ICollection<HashDefault> HashDefaults { get; set; }
        = new List<HashDefault>();
}