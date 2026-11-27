
using Microsoft.EntityFrameworkCore;

using Pgvector;
using Pgvector.EntityFrameworkCore;

using FileSyncX.EfCore.Contexts;
using FileSyncX.EfCore.Models;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Repositories;

public sealed class ArchiveHashCategorizationRepository
    : IArchiveHashCategorizationRepository
{
    private readonly IDbContextFactory<VecktorVaultDbContext> _dbFactory;

    public ArchiveHashCategorizationRepository(
        IDbContextFactory<VecktorVaultDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // =========================================================
    // BUSCAR POR ID
    // =========================================================

    public async Task<ArchiveHashCategorization?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.ArchiveHashCategorizations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    // =========================================================
    // BUSCAR POR DOCUMENT UUID
    // =========================================================

    public async Task<ArchiveHashCategorization?> GetByDocumentUuidAsync(
        Guid documentUuid,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.ArchiveHashCategorizations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.DocumentUuid == documentUuid,
                cancellationToken);
    }

    // =========================================================
    // BUSCAR POR SHA256
    // =========================================================

    public async Task<IReadOnlyList<ArchiveHashCategorization>>
        GetBySha256Async(
            string sha256,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);

        RepositoryValidation.ValidateSha256(sha256);

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.ArchiveHashCategorizations
            .AsNoTracking()
            .Where(x => x.Sha256 == sha256)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    // =========================================================
    // BUSCAR DOCUMENTOS POR CATEGORIA
    // =========================================================

    public async Task<IReadOnlyList<ArchiveHashCategorization>>
        GetByCategoryIdAsync(
            int categoryId,
            CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.ArchiveHashCategorizations
            .AsNoTracking()
            .Where(x => x.IdHashDefault == categoryId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    // =========================================================
    // BUSCAR DOCUMENTOS PENDENTES
    // =========================================================

    public async Task<IReadOnlyList<ArchiveHashCategorization>>
        GetPendingAsync(
            int limit = 100,
            CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "O limite deve ser maior que zero.");
        }

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        return await context.ArchiveHashCategorizations
            .AsNoTracking()
            .Where(x =>
                x.ClassificationStatus ==
                ArchiveClassificationStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    // =========================================================
    // ADICIONAR DOCUMENTO
    // =========================================================

    public async Task<ArchiveHashCategorization> AddAsync(
        ArchiveHashCategorization document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        RepositoryValidation.ValidateSha256(document.Sha256);

        RepositoryValidation.ValidateClassificationStatus(
            document.ClassificationStatus);

        if (document.Embedding is not null)
        {
            RepositoryValidation.ValidateEmbedding(
                document.Embedding);
        }

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        if (document.DocumentUuid == Guid.Empty)
        {
            document.DocumentUuid = Guid.NewGuid();
        }

        if (document.CreatedAt == default)
        {
            document.CreatedAt = DateTimeOffset.UtcNow;
        }

        context.ArchiveHashCategorizations.Add(document);

        await context.SaveChangesAsync(cancellationToken);

        return document;
    }

    // =========================================================
    // ATUALIZAR DOCUMENTO
    // =========================================================

    public async Task<bool> UpdateAsync(
        ArchiveHashCategorization document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        RepositoryValidation.ValidateSha256(document.Sha256);

        RepositoryValidation.ValidateClassificationStatus(
            document.ClassificationStatus);

        if (document.Embedding is not null)
        {
            RepositoryValidation.ValidateEmbedding(
                document.Embedding);
        }

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await context.ArchiveHashCategorizations
            .FirstOrDefaultAsync(
                x => x.Id == document.Id,
                cancellationToken);

        if (existing is null)
        {
            return false;
        }

        // Atualiza somente as propriedades editáveis.

        existing.Sha256 = document.Sha256;

        existing.SourceFile = document.SourceFile;

        existing.Embedding = document.Embedding;

        existing.DestinationFile = document.DestinationFile;

        existing.DestinationFolder = document.DestinationFolder;

        existing.IdHashDefault = document.IdHashDefault;

        existing.ClassificationStatus = document.ClassificationStatus;

        existing.SimilarityScore = document.SimilarityScore;

        existing.ClassifiedAt = document.ClassifiedAt;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // =========================================================
    // ATUALIZAR RESULTADO DA CLASSIFICAÇÃO
    // =========================================================

    public async Task<bool> UpdateClassificationAsync(
        Guid documentUuid,
        int? categoryId,
        string classificationStatus,
        double? similarityScore,
        string? destinationFile,
        string? destinationFolder,
        CancellationToken cancellationToken = default)
    {
        RepositoryValidation.ValidateClassificationStatus(
            classificationStatus);

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        var document = await context.ArchiveHashCategorizations
            .FirstOrDefaultAsync(
                x => x.DocumentUuid == documentUuid,
                cancellationToken);

        if (document is null)
        {
            return false;
        }

        // Categoria identificada
        document.IdHashDefault = categoryId;

        // Estado da classificação
        document.ClassificationStatus = classificationStatus;

        // Pontuação de similaridade
        document.SimilarityScore = similarityScore;

        // Destinos
        document.DestinationFile = destinationFile;

        document.DestinationFolder = destinationFolder;

        // Data da classificação
        document.ClassifiedAt =
            classificationStatus == ArchiveClassificationStatus.Pending
                ? null
                : DateTimeOffset.UtcNow;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // =========================================================
    // EXCLUIR DOCUMENTO
    // =========================================================

    public async Task<bool> DeleteAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        var document = await context.ArchiveHashCategorizations
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);

        if (document is null)
        {
            return false;
        }

        context.ArchiveHashCategorizations.Remove(document);

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    // =========================================================
    // PESQUISA VETORIAL DE DOCUMENTOS
    // =========================================================

    public async Task<IReadOnlyList<
        VectorSearchResult<ArchiveHashCategorization>>>
        SearchSimilarAsync(
            Vector embedding,
            int limit = 10,
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

        var results = await context.ArchiveHashCategorizations
            .AsNoTracking()

            // O índice HNSW foi criado com filtro
            // embedding IS NOT NULL.
            .Where(x => x.Embedding != null)

            // Ordena pela menor distância de cosseno.
            .OrderBy(x =>
                x.Embedding!.CosineDistance(embedding))

            .Take(limit)

            .Select(x => new
            {
                Entity = x,

                Distance = x.Embedding!
                    .CosineDistance(embedding)
            })

            .ToListAsync(cancellationToken);

        return results
            .Select(x =>
                new VectorSearchResult<ArchiveHashCategorization>(
                    x.Entity,
                    x.Distance,
                    1.0 - x.Distance))
            .ToList();
    }
}
