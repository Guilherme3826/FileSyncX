
using Microsoft.EntityFrameworkCore;

using Pgvector;

using FileSyncX.EfCore.Contexts;
using FileSyncX.EfCore.Models;
using Pgvector.EntityFrameworkCore;

namespace FileSyncX.Services;

public sealed class VecktorVaultService : IVecktorVaultService
{
    private const int EmbeddingDimensions = 1536;

    private readonly IDbContextFactory<VecktorVaultDbContext> _dbFactory;

    public VecktorVaultService(
        IDbContextFactory<VecktorVaultDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // =========================================================
    // VALIDAÇÃO E CONVERSÃO DE EMBEDDING
    // =========================================================

    private static Vector CreateVector(float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Length != EmbeddingDimensions)
        {
            throw new ArgumentException(
                $"O embedding deve possuir {EmbeddingDimensions} dimensões.",
                nameof(values));
        }

        if (values.Any(value => !float.IsFinite(value)))
        {
            throw new ArgumentException(
                "O embedding contém valores não finitos.",
                nameof(values));
        }

        return new Vector(values);
    }

    // =========================================================
    // CRIAR CATEGORIA
    // =========================================================

    public async Task<HashDefault> CreateCategoryAsync(
        string description,
        string destinationFolder,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFolder);

        var category = new HashDefault
        {
            Description = description,
            DestinationFolder = destinationFolder,
            Embedding = CreateVector(embedding)
        };

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        context.HashDefaults.Add(category);

        await context.SaveChangesAsync(cancellationToken);

        return category;
    }

    // =========================================================
    // REGISTRAR DOCUMENTO
    // =========================================================

    public async Task<ArchiveHashCategorization> RegisterDocumentAsync(
        string sourceFile,
        string? sha256,
        float[]? embedding = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        if (sha256 is not null &&
            (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit)))
        {
            throw new ArgumentException(
                "O SHA-256 deve possuir exatamente 64 caracteres hexadecimais.",
                nameof(sha256));
        }

        var document = new ArchiveHashCategorization
        {
            DocumentUuid = Guid.NewGuid(),
            SourceFile = sourceFile,
            Sha256 = sha256,
            Embedding = embedding is null
                ? null
                : CreateVector(embedding),

            ClassificationStatus = "pending"
        };

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        context.ArchiveHashCategorizations.Add(document);

        await context.SaveChangesAsync(cancellationToken);

        return document;
    }

    // =========================================================
    // PESQUISA SEMÂNTICA DE CATEGORIAS
    // =========================================================

    public async Task<IReadOnlyList<HashDefaultSearchResult>>
        SearchCategoriesAsync(
            float[] embedding,
            int limit = 5,
            CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "O limite deve ser maior que zero.");
        }

        var queryVector = CreateVector(embedding);

        await using var context =
            await _dbFactory.CreateDbContextAsync(cancellationToken);

        var results = await context.HashDefaults
            .AsNoTracking()
            .Select(category => new
            {
                Category = category,

                Distance = category.Embedding
                    .CosineDistance(queryVector)
            })
            .OrderBy(x => x.Distance)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results
            .Select(x => new HashDefaultSearchResult(
                x.Category,
                x.Distance,
                1.0 - x.Distance))
            .ToList();
    }
}
