using Microsoft.EntityFrameworkCore;

using Pgvector;
using Pgvector.EntityFrameworkCore;

using FileSyncX.EfCore.Contexts;
using FileSyncX.EfCore.Models;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Repositories;

public sealed class HashDefaultRepository : IHashDefaultRepository
{
    private readonly IDbContextFactory<VecktorVaultDbContext> _dbFactory;

    public HashDefaultRepository(
        IDbContextFactory<VecktorVaultDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // =========================================================
    // BUSCAR POR ID
    // =========================================================

    public async Task<HashDefault?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.HashDefaults
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    // =========================================================
    // BUSCAR CATEGORIA COM DOCUMENTOS
    // =========================================================

    public async Task<HashDefault?> GetByIdWithDocumentsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.HashDefaults
            .AsNoTracking()
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    // =========================================================
    // LISTAR TODAS AS CATEGORIAS
    // =========================================================

    public async Task<IReadOnlyList<HashDefault>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.HashDefaults
            .AsNoTracking()
            .OrderBy(x => x.Description)
            .ToListAsync(cancellationToken);
    }

    // =========================================================
    // ADICIONAR CATEGORIA
    // =========================================================

    public async Task<HashDefault> AddAsync(
        HashDefault category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        RepositoryValidation.ValidateEmbedding(
            category.Embedding);

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (category.CreatedAt == default)
        {
            category.CreatedAt = DateTimeOffset.UtcNow;
        }

        context.HashDefaults.Add(category);

        await context.SaveChangesAsync(cancellationToken);

        return category;
    }

    // =========================================================
    // ATUALIZAR CATEGORIA
    // =========================================================

    public async Task<bool> UpdateAsync(
        HashDefault category,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        RepositoryValidation.ValidateEmbedding(
            category.Embedding);

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.HashDefaults
            .FirstOrDefaultAsync(
                x => x.Id == category.Id,
                cancellationToken);

        if (existing is null)
        {
            return false;
        }

        // Atualiza somente as propriedades editáveis.
        existing.Description = category.Description;

        existing.DestinationFolder = category.DestinationFolder;

        existing.Embedding = category.Embedding;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // =========================================================
    // EXCLUIR CATEGORIA
    // =========================================================

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        var category = await context.HashDefaults
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (category is null)
        {
            return false;
        }

        context.HashDefaults.Remove(category);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // =========================================================
    // PESQUISA SEMÂNTICA POR SIMILARIDADE
    // =========================================================

    public async Task<IReadOnlyList<VectorSearchResult<HashDefault>>>
        SearchSimilarAsync(
            Vector embedding,
            int limit = 5,
            CancellationToken cancellationToken = default)
    {
        RepositoryValidation.ValidateEmbedding(embedding);

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "O limite deve ser maior que zero.");
        }

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        var results = await context.HashDefaults
            .AsNoTracking()

            // Ordena pela menor distância de cosseno
            .OrderBy(x =>
                x.Embedding.CosineDistance(embedding))

            .Take(limit)

            .Select(x => new
            {
                Entity = x,

                Distance = x.Embedding.CosineDistance(embedding)
            })

            .ToListAsync(cancellationToken);

        return results
            .Select(x => new VectorSearchResult<HashDefault>(
                x.Entity,
                x.Distance,
                1.0 - x.Distance))
            .ToList();
    }
}