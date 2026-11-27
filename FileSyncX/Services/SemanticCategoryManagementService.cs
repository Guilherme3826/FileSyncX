using FileSyncX.EfCore.Contexts;
using FileSyncX.EfCore.Models;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FileSyncX.Services;

/// <summary>
/// CRUD de categorias semânticas em public.tbl_hash_default.
/// O embedding é regenerado sempre que a descrição é criada ou alterada.
/// </summary>
public sealed class SemanticCategoryManagementService : ISemanticCategoryManagementService
{
    private const int EmbeddingDimensions = 1536;

    private readonly IDbContextFactory<VecktorVaultDbContext> _dbFactory;
    private readonly IEmbeddingService _embeddingService;

    public SemanticCategoryManagementService(
        IDbContextFactory<VecktorVaultDbContext> dbFactory,
        IEmbeddingService embeddingService)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    public async Task<IReadOnlyList<SemanticCategoryListItem>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.HashDefaults
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Description,
                x.DestinationFolder,
                x.CreatedAt,
                DocumentCount = db.ArchiveHashCategorizations
                    .Count(document => document.IdHashDefault == x.Id)
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row =>
        {
            var parsed = ParseStoredDescription(row.Description);
            return new SemanticCategoryListItem(
                row.Id,
                parsed.Name,
                parsed.Description,
                parsed.Keywords,
                row.Description,
                row.DestinationFolder,
                row.DocumentCount,
                row.CreatedAt);
        }).ToList();
    }

    public async Task<int> SaveAsync(
        int? categoryId,
        string categoryName,
        string semanticDescription,
        string keywords,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            throw new ArgumentException("Informe o nome da categoria.", nameof(categoryName));

        if (string.IsNullOrWhiteSpace(semanticDescription))
            throw new ArgumentException("A descrição semântica não pode ficar vazia.", nameof(semanticDescription));

        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentException("Informe a pasta de destino.", nameof(destinationFolder));

        string folder = Path.GetFullPath(destinationFolder.Trim());
        string storedDescription = BuildStoredDescription(
            categoryName,
            semanticDescription,
            keywords);

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        bool duplicate = await db.HashDefaults
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id != (categoryId ?? 0) &&
                x.Description == storedDescription &&
                x.DestinationFolder == folder,
                cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException(
                "Já existe uma categoria com a mesma descrição e pasta de destino.");
        }

        // O texto usado no embedding inclui nome, descrição e termos de busca.
        float[] embeddingValues = await _embeddingService.GenerateEmbeddingAsync(
            storedDescription,
            cancellationToken);

        ValidateEmbedding(embeddingValues);
        var vector = new Vector(embeddingValues);

        if (categoryId.HasValue)
        {
            HashDefault? entity = await db.HashDefaults
                .FirstOrDefaultAsync(x => x.Id == categoryId.Value, cancellationToken);

            if (entity is null)
                throw new InvalidOperationException("A categoria selecionada não existe mais no banco de dados.");

            entity.Description = storedDescription;
            entity.DestinationFolder = folder;
            entity.Embedding = vector;
        }
        else
        {
            var entity = new HashDefault
            {
                Description = storedDescription,
                DestinationFolder = folder,
                Embedding = vector,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.HashDefaults.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
            return entity.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return categoryId.Value;
    }

    public async Task<HashDefaultSimilarityResult> AssociateDocumentToCategoryAsync(
        long documentId,
        int categoryId,
        string destinationFolder,
        string? destinationFile,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentException("A pasta de destino é obrigatória.", nameof(destinationFolder));

        string folder = Path.GetFullPath(destinationFolder.Trim());
        string? normalizedDestinationFile = string.IsNullOrWhiteSpace(destinationFile)
            ? null
            : Path.GetFullPath(destinationFile.Trim());

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        HashDefault? category = await db.HashDefaults
            .FirstOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null)
            throw new KeyNotFoundException($"Categoria {categoryId} não encontrada.");

        ArchiveHashCategorization? document = await db.ArchiveHashCategorizations
            .FirstOrDefaultAsync(x => x.Id == documentId, cancellationToken);

        if (document is null)
            throw new KeyNotFoundException($"Documento {documentId} não encontrado.");

        if (document.Embedding is null)
            throw new InvalidOperationException("O documento não possui embedding persistido.");

        // Calcula a distância cosseno no PostgreSQL usando os embeddings já persistidos.
        double distance = await db.ArchiveHashCategorizations
            .AsNoTracking()
            .Where(x => x.Id == documentId)
            .Select(x => x.Embedding!.CosineDistance(category.Embedding))
            .SingleAsync(cancellationToken);

        double similarity = Math.Clamp(1d - distance, -1d, 1d);

        category.DestinationFolder = folder;

        document.IdHashDefault = category.Id;
        document.SimilarityScore = similarity;
        document.ClassificationStatus = "classified";
        document.DestinationFolder = folder;
        document.DestinationFile = normalizedDestinationFile;
        document.ClassifiedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new HashDefaultSimilarityResult(
            category.Id,
            category.Description,
            category.DestinationFolder,
            distance,
            similarity);
    }

    public async Task DeleteAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        HashDefault? category = await db.HashDefaults
            .FirstOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null)
            return;

        // Preserva os documentos: remove somente a associação com a categoria.
        await db.ArchiveHashCategorizations
            .Where(x => x.IdHashDefault == categoryId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.IdHashDefault, (int?)null),
                cancellationToken);

        db.HashDefaults.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }


    public async Task<int> ReclassifyWithoutCategoryAsync(
        double minimumSimilarity = 0.75,
        CancellationToken cancellationToken = default)
    {
        if (double.IsNaN(minimumSimilarity) ||
            double.IsInfinity(minimumSimilarity) ||
            minimumSimilarity < 0 || minimumSimilarity > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSimilarity),
                "A similaridade mínima deve estar entre 0 e 1.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        bool hasCategories = await db.HashDefaults.AnyAsync(cancellationToken);
        if (!hasCategories)
            return 0;

        // Inclui documentos anteriormente classificados cuja categoria foi
        // removida (FK SET NULL), além de documentos pending/unclassified.
        var documents = await db.ArchiveHashCategorizations
            .Where(x => x.IdHashDefault == null && x.Embedding != null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        int processed = 0;

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var best = await db.HashDefaults
                .AsNoTracking()
                .Select(category => new
                {
                    category.Id,
                    category.Description,
                    category.DestinationFolder,
                    Distance = category.Embedding.CosineDistance(document.Embedding!)
                })
                .OrderBy(x => x.Distance)
                .FirstOrDefaultAsync(cancellationToken);

            if (best is null)
            {
                document.ClassificationStatus = "unclassified";
                document.IdHashDefault = null;
                document.SimilarityScore = null;
                document.DestinationFolder = null;
                document.ClassifiedAt = DateTimeOffset.UtcNow;
                continue;
            }

            double similarity = Math.Clamp(1d - best.Distance, 0d, 1d);
            bool classified = similarity >= minimumSimilarity;

            // A melhor categoria fica registrada mesmo em review, preservando
            // a sugestão para a tela. Só associa pasta de destino ao classificado.
            document.IdHashDefault = best.Id;
            document.SimilarityScore = similarity;
            document.ClassificationStatus = classified ? "classified" : "review";
            document.DestinationFolder = classified ? best.DestinationFolder : null;
            document.ClassifiedAt = DateTimeOffset.UtcNow;
            processed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return processed;
    }

    private static string BuildStoredDescription(
        string categoryName,
        string semanticDescription,
        string keywords)
    {
        string result =
            $"Categoria: {categoryName.Trim()}\n\n" +
            $"Descrição semântica:\n{semanticDescription.Trim()}";

        if (!string.IsNullOrWhiteSpace(keywords))
            result += $"\n\nTermos e elementos associados:\n{keywords.Trim()}";

        return result;
    }

    private static (string Name, string Description, string Keywords) ParseStoredDescription(string stored)
    {
        const string namePrefix = "Categoria:";
        const string descriptionPrefix = "Descrição semântica:";

        if (stored.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = stored.Split(
                new[] { descriptionPrefix },
                2,
                StringSplitOptions.None);

            string name = parts[0][namePrefix.Length..].Trim();
            string description = parts.Length > 1
                ? parts[1].Split(
                    new[] { "\n\nTermos e elementos associados:" },
                    2,
                    StringSplitOptions.None)[0].Trim()
                : stored;

            string keywords = string.Empty;
            int keywordsIndex = stored.IndexOf(
                "Termos e elementos associados:",
                StringComparison.OrdinalIgnoreCase);
            if (keywordsIndex >= 0)
            {
                keywords = stored[(keywordsIndex + "Termos e elementos associados:".Length)..].Trim();
            }

            return (name, description, keywords);
        }

        // Compatibilidade com categorias cadastradas antes desta tela.
        string firstLine = stored.Split('\n', 2)[0].Trim();
        string fallbackName = firstLine.Length > 90
            ? firstLine[..87] + "..."
            : firstLine;

        return (fallbackName, stored, string.Empty);
    }

    private static void ValidateEmbedding(float[]? embedding)
    {
        if (embedding is null || embedding.Length != EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"O serviço de embeddings deve retornar exatamente {EmbeddingDimensions} dimensões.");
        }

        if (embedding.Any(value => !float.IsFinite(value)))
            throw new InvalidOperationException("O embedding contém valores inválidos.");
    }
}
