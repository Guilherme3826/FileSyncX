using Microsoft.EntityFrameworkCore;

using FileSyncX.EfCore.Models;

namespace FileSyncX.EfCore.Contexts;

public sealed class VecktorVaultDbContext
    : DbContext
{
    public VecktorVaultDbContext(
        DbContextOptions<VecktorVaultDbContext> options)
        : base(options)
    {
    }

    // =========================================================
    // DBSETS
    // =========================================================

    public DbSet<HashDefault> HashDefaults
        => Set<HashDefault>();

    public DbSet<ArchiveHashCategorization> ArchiveHashCategorizations
        => Set<ArchiveHashCategorization>();

    // =========================================================
    // MODEL CONFIGURATION
    // =========================================================

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Schema padrão
        modelBuilder.HasDefaultSchema("public");

        // Habilita o reconhecimento da extensão pgvector
        modelBuilder.HasPostgresExtension("vector", "public");

        // Carrega as configurações das entidades
        // presentes no assembly atual.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(VecktorVaultDbContext).Assembly);
    }
}