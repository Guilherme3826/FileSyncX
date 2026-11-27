using FileSyncX.EfCore.Contexts;
using FileSyncX.EfCore.Models;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FileSyncX.Services;

/// <summary>
/// Gerencia as categorias semânticas armazenadas em public.tbl_hash_default.
/// O embedding da categoria é regenerado sempre que ela é criada ou alterada.
/// </summary>
public sealed class SemanticCategoryManagementService : ISemanticCategoryManagementService
{
    private const int EmbeddingDimensions = 1536;
    private const string NamePrefix = "Categoria:";
    private const string DescriptionPrefix = "Descrição semântica:";
    private const string KeywordsPrefix = "Termos e elementos associados:";

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

        string normalizedName = categoryName.Trim();
        string normalizedDescription = NormalizeMultilineText(semanticDescription);
        string normalizedKeywords = NormalizeKeywordText(keywords);
        string folder = Path.GetFullPath(destinationFolder.Trim());

        // Este é o texto canônico: exatamente o mesmo conteúdo é salvo no banco
        // e enviado ao modelo de embeddings.
        string storedDescription = BuildStoredDescription(
            normalizedName,
            normalizedDescription,
            normalizedKeywords);

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

    public async Task DeleteAsync(
        int categoryId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        HashDefault? category = await db.HashDefaults
            .FirstOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null)
            return;

        // Preserva os documentos: remove apenas a associação com a categoria.
        await db.ArchiveHashCategorizations
            .Where(x => x.IdHashDefault == categoryId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.IdHashDefault, (int?)null),
                cancellationToken);

        db.HashDefaults.Remove(category);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Reclassifica TODOS os documentos com embedding, mesmo aqueles que já
    /// possuem id_hash_default. Isso é necessário após editar uma categoria,
    /// pois o embedding dela é atualizado e as pontuações antigas ficam obsoletas.
    /// </summary>
    public async Task<int> ReclassifyAllAsync(
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

        // Importante: não filtrar por IdHashDefault == null.
        // Documentos em review também recebem a categoria candidata e, por isso,
        // eram ignorados pela implementação anterior.
        var documents = await db.ArchiveHashCategorizations
            .Where(x => x.Embedding != null)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);

        if (documents.Count == 0)
            return 0;

        bool hasCategories = await db.HashDefaults.AnyAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (ArchiveHashCategorization document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!hasCategories)
            {
                document.IdHashDefault = null;
                document.SimilarityScore = null;
                document.ClassificationStatus = "unclassified";
                document.DestinationFolder = null;
                document.ClassifiedAt = now;
                continue;
            }

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
                document.IdHashDefault = null;
                document.SimilarityScore = null;
                document.ClassificationStatus = "unclassified";
                document.DestinationFolder = null;
                document.ClassifiedAt = now;
                continue;
            }

            double similarity = Math.Clamp(1d - best.Distance, -1d, 1d);
            bool approved = similarity >= minimumSimilarity;

            // Mantém a melhor categoria como sugestão, inclusive em review.
            // A pasta só é preenchida quando o limite é atingido.
            document.IdHashDefault = best.Id;
            document.SimilarityScore = similarity;
            document.ClassificationStatus = approved ? "classified" : "review";
            document.DestinationFolder = approved ? best.DestinationFolder : null;
            document.ClassifiedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return documents.Count;
    }

    private static string BuildStoredDescription(
        string categoryName,
        string semanticDescription,
        string keywords)
    {
        string result =
            $"{NamePrefix} {categoryName.Trim()}\n\n" +
            $"{DescriptionPrefix}\n{semanticDescription.Trim()}";

        if (!string.IsNullOrWhiteSpace(keywords))
            result += $"\n\n{KeywordsPrefix}\n{keywords.Trim()}";

        return result;
    }

    private static string NormalizeMultilineText(string value)
    {
        return string.Join(
            '\n',
            value.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => string.Join(' ', line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries)))
                .Where(line => line.Length > 0));
    }

    private static string NormalizeKeywordText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            "; ",
            value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(term => term.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static (string Name, string Description, string Keywords) ParseStoredDescription(string stored)
    {
        if (stored.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = stored.Split(
                new[] { DescriptionPrefix },
                2,
                StringSplitOptions.None);

            string name = parts[0][NamePrefix.Length..].Trim();
            string description = parts.Length > 1
                ? parts[1].Split(
                    new[] { $"\n\n{KeywordsPrefix}" },
                    2,
                    StringSplitOptions.None)[0].Trim()
                : stored;

            string keywords = string.Empty;
            int keywordsIndex = stored.IndexOf(
                KeywordsPrefix,
                StringComparison.OrdinalIgnoreCase);

            if (keywordsIndex >= 0)
                keywords = stored[(keywordsIndex + KeywordsPrefix.Length)..].Trim();

            return (name, description, keywords);
        }

        // Compatibilidade com categorias criadas antes do gerenciador semântico.
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
            throw new InvalidOperationException("O serviço de embeddings retornou valores não finitos.");
    }
}
