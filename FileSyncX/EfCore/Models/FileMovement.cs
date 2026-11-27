namespace FileSyncX.EfCore.Models;

public class FileMovement
{
    public int Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string DestinationPath { get; set; } = string.Empty;

    public DateTimeOffset MovedAt { get; set; }

    // FK para HashDefault
    public int? IdHashDefault { get; set; }

    // Propriedade de navegação
    public HashDefault? HashDefault { get; set; }
}