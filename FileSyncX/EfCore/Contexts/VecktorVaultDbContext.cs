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

        // Registra a extensão pgvector
        modelBuilder.HasPostgresExtension("vector");

        // Carrega as configurações das entidades
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(VecktorVaultDbContext).Assembly);
    }
}