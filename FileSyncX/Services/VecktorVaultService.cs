
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FileSyncX.EfCore.Contexts;
using FileSyncX.EfCore.Models;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

using Microsoft.EntityFrameworkCore;

using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FileSyncX.Services;

public sealed class VecktorVaultService : IVecktorVaultService
{
    private const int EmbeddingDimensions = 1536;

    // Configuração da segmentação de documentos.
    private const int MaxCaracteresPorTrecho = 6500;
    private const int SobreposicaoCaracteres = 400;

    private readonly IDbContextFactory<VecktorVaultDbContext> _dbFactory;
    private readonly IEmbeddingService _embeddingService;

    // =========================================================
    // CONSTRUTOR
    // =========================================================

    public VecktorVaultService(
        IDbContextFactory<VecktorVaultDbContext> dbFactory,
        IEmbeddingService embeddingService)
    {
        _dbFactory = dbFactory
            ?? throw new ArgumentNullException(nameof(dbFactory));

        _embeddingService = embeddingService
            ?? throw new ArgumentNullException(nameof(embeddingService));
    }

    // =========================================================
    // CONVERTER E VALIDAR EMBEDDING
    // =========================================================

    private static Vector CriarVector(float[] embedding)
    {
        if (embedding is null ||
            embedding.Length != EmbeddingDimensions)
        {
            throw new InvalidOperationException(
                $"Embedding inválido. Esperado: {EmbeddingDimensions} dimensões."
            );
        }

        if (embedding.Any(x => !float.IsFinite(x)))
        {
            throw new InvalidOperationException(
                "O embedding contém valores não finitos."
            );
        }

        return new Vector(embedding);
    }

    // =========================================================
    // VALIDAR SHA-256
    // =========================================================

    private static string NormalizarSha256(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
        {
            throw new ArgumentException(
                "SHA-256 não informado.",
                nameof(sha256)
            );
        }

        sha256 = sha256.Trim();

        if (sha256.Length != 64 ||
            !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException(
                "SHA-256 inválido. São necessários 64 caracteres hexadecimais.",
                nameof(sha256)
            );
        }

        return sha256.ToLowerInvariant();
    }

    // =========================================================
    // GERAR EMBEDDING REPRESENTATIVO DO DOCUMENTO
    // =========================================================

    private async Task<float[]> GerarEmbeddingDocumentoAsync(
        string texto,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ArgumentException(
                "Não é possível gerar embedding de um texto vazio.",
                nameof(texto)
            );
        }

        // 1. Divide o documento em trechos.
        var trechos = DividirTextoEmTrechos(texto);

        if (trechos.Count == 0)
        {
            throw new InvalidOperationException(
                "Não foi possível preparar trechos para embedding."
            );
        }

        // 2. Acumulador para a média ponderada.
        var acumulador = new double[EmbeddingDimensions];

        double pesoTotal = 0;

        // 3. Gera um embedding para cada trecho.
        for (int i = 0; i < trechos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            float[] vetor = await _embeddingService.GenerateEmbeddingAsync(
                trechos[i],
                cancellationToken
            );

            if (vetor is null ||
                vetor.Length != EmbeddingDimensions)
            {
                throw new InvalidOperationException(
                    $"Trecho {i + 1}: esperado embedding com " +
                    $"{EmbeddingDimensions} dimensões."
                );
            }

            if (vetor.Any(x => !float.IsFinite(x)))
            {
                throw new InvalidOperationException(
                    $"Trecho {i + 1}: o embedding contém valores não finitos."
                );
            }

            // O peso considera o tamanho do trecho.
            double peso = trechos[i].Length;

            for (int j = 0; j < EmbeddingDimensions; j++)
            {
                acumulador[j] += vetor[j] * peso;
            }

            pesoTotal += peso;
        }

        if (pesoTotal <= 0)
        {
            throw new InvalidOperationException(
                "Não foi possível calcular a média dos embeddings."
            );
        }

        // 4. Calcula a média ponderada.
        var resultado = new float[EmbeddingDimensions];

        double somaQuadrados = 0;

        for (int i = 0; i < EmbeddingDimensions; i++)
        {
            resultado[i] = (float)(acumulador[i] / pesoTotal);

            somaQuadrados += resultado[i] * resultado[i];
        }

        // 5. Normaliza o vetor resultante.
        double norma = Math.Sqrt(somaQuadrados);

        if (norma <= 0 || !double.IsFinite(norma))
        {
            throw new InvalidOperationException(
                "O vetor agregado possui norma inválida."
            );
        }

        for (int i = 0; i < resultado.Length; i++)
        {
            resultado[i] = (float)(resultado[i] / norma);
        }

        return resultado;
    }

    // =========================================================
    // DIVIDIR TEXTO EM TRECHOS COM SOBREPOSIÇÃO
    // =========================================================

    private static List<string> DividirTextoEmTrechos(string texto)
    {
        var trechos = new List<string>();

        int inicio = 0;

        while (inicio < texto.Length)
        {
            int fim = Math.Min(
                inicio + MaxCaracteresPorTrecho,
                texto.Length
            );

            if (fim < texto.Length)
            {
                char[] separadores =
                {
                    '\n',
                    ' ',
                    '.',
                    ';'
                };

                int separador = texto.LastIndexOfAny(
                    separadores,
                    fim - 1,
                    fim - inicio
                );

                // Evita gerar fragmentos excessivamente pequenos.
                if (separador >
                    inicio + (int)(MaxCaracteresPorTrecho * 0.70))
                {
                    fim = separador + 1;
                }

                // Não divide um par surrogate UTF-16.
                if (fim < texto.Length &&
                    fim > inicio &&
                    char.IsHighSurrogate(texto[fim - 1]) &&
                    char.IsLowSurrogate(texto[fim]))
                {
                    fim--;
                }
            }

            string trecho = texto[inicio..fim].Trim();

            if (!string.IsNullOrWhiteSpace(trecho))
            {
                trechos.Add(trecho);
            }

            if (fim >= texto.Length)
                break;

            inicio = Math.Max(
                inicio + 1,
                fim - SobreposicaoCaracteres
            );

            while (inicio < texto.Length &&
                   char.IsWhiteSpace(texto[inicio]))
            {
                inicio++;
            }
        }

        return trechos;
    }

    // =========================================================
    // CRIAR OU OBTER CATEGORIA DE REFERÊNCIA
    // =========================================================

    public async Task<int> CriarOuObterCategoriaAsync(
        string description,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException(
                "A descrição da categoria é obrigatória.",
                nameof(description)
            );
        }

        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            throw new ArgumentException(
                "A pasta de destino é obrigatória.",
                nameof(destinationFolder)
            );
        }

        await using var db = await _dbFactory.CreateDbContextAsync(
            cancellationToken
        );

        // Verifica se a categoria já existe.
        var categoriaExistente = await db.HashDefaults
            .FirstOrDefaultAsync(
                x => x.Description == description &&
                     x.DestinationFolder == destinationFolder,
                cancellationToken
            );

        if (categoriaExistente is not null)
        {
            return categoriaExistente.Id;
        }

        // Gera embedding da descrição da categoria.
        float[] valores = await _embeddingService.GenerateEmbeddingAsync(
            description,
            cancellationToken
        );

        var categoria = new HashDefault
        {
            Description = description.Trim(),
            DestinationFolder = destinationFolder.Trim(),
            Embedding = CriarVector(valores)
        };

        db.HashDefaults.Add(categoria);

        await db.SaveChangesAsync(cancellationToken);

        return categoria.Id;
    }

    // =========================================================
    // INDEXAR DOCUMENTO
    // =========================================================

    public async Task<long> IndexarDocumentoAsync(
        string sourceFile,
        string textoExtraido,
        string? sha256 = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFile))
        {
            throw new ArgumentException(
                "Arquivo de origem obrigatório.",
                nameof(sourceFile)
            );
        }

        if (string.IsNullOrWhiteSpace(textoExtraido))
        {
            throw new ArgumentException(
                "Texto extraído obrigatório.",
                nameof(textoExtraido)
            );
        }

        if (sha256 is not null)
        {
            sha256 = NormalizarSha256(sha256);

            // Primeira verificação de duplicidade.
            long? documentoExistente =
                await BuscarDocumentoPorSha256Async(
                    sha256,
                    cancellationToken
                );

            if (documentoExistente.HasValue)
            {
                return documentoExistente.Value;
            }
        }

        // Gera o vetor representativo, inclusive para PDFs extensos.
        float[] valores = await GerarEmbeddingDocumentoAsync(
            textoExtraido,
            cancellationToken
        );

        var embedding = CriarVector(valores);

        await using var db = await _dbFactory.CreateDbContextAsync(
            cancellationToken
        );

        // Segunda verificação antes da inserção.
        if (sha256 is not null)
        {
            long? documentoExistente = await db.ArchiveHashCategorizations
                .Where(x => x.Sha256 == sha256)
                .Select(x => (long?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (documentoExistente.HasValue)
            {
                return documentoExistente.Value;
            }
        }

        var documento = new ArchiveHashCategorization
        {
            SourceFile = sourceFile,
            Sha256 = sha256,
            Embedding = embedding,
            ClassificationStatus = "pending"
        };

        db.ArchiveHashCategorizations.Add(documento);

        await db.SaveChangesAsync(cancellationToken);

        return documento.Id;
    }

    // =========================================================
    // BUSCAR DOCUMENTO PELO SHA-256
    // =========================================================

    public async Task<long?> BuscarDocumentoPorSha256Async(
        string sha256,
        CancellationToken cancellationToken = default)
    {
        sha256 = NormalizarSha256(sha256);

        await using var db = await _dbFactory.CreateDbContextAsync(
            cancellationToken
        );

        return await db.ArchiveHashCategorizations
            .AsNoTracking()
            .Where(x => x.Sha256 == sha256)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    // =========================================================
    // OBTER DOCUMENTO
    // =========================================================

    public async Task<ArchiveHashCategorization?> ObterDocumentoAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(
            cancellationToken
        );

        return await db.ArchiveHashCategorizations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken
            );
    }

    // =========================================================
    // REGISTRAR DESTINO DO DOCUMENTO
    // =========================================================

    public async Task RegistrarDestinoDocumentoAsync(
        long documentId,
        string destinationFile,
        string destinationFolder,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationFile))
        {
            throw new ArgumentException(
                "Caminho do arquivo de destino obrigatório.",
                nameof(destinationFile)
            );
        }

        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            throw new ArgumentException(
                "Pasta de destino obrigatória.",
                nameof(destinationFolder)
            );
        }

        await using var db = await _dbFactory.CreateDbContextAsync(
            cancellationToken
        );

        var documento = await db.ArchiveHashCategorizations
            .FirstOrDefaultAsync(
                x => x.Id == documentId,
                cancellationToken
            );

        if (documento is null)
        {
            throw new KeyNotFoundException(
                $"Documento {documentId} não encontrado."
            );
        }

        documento.DestinationFile = destinationFile;
        documento.DestinationFolder = destinationFolder;

        await db.SaveChangesAsync(cancellationToken);
    }

    // =========================================================
    // BUSCAR CATEGORIAS SEMELHANTES A UM TEXTO
    // =========================================================

    public async Task<IReadOnlyList<HashDefaultSimilarityResult>>
        BuscarCategoriasSemelhantesAsync(
            string texto,
            int quantidade = 5,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            throw new ArgumentException(
                "Texto de pesquisa obrigatório.",
                nameof(texto)
            );
        }

        if (quantidade < 1 || quantidade > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantidade),
                "A quantidade deve estar entre 1 e 100."
            );
        }

        // Gera embedding do texto de pesquisa.
        float[] valores = await _embeddingService.GenerateEmbeddingAsync(
            texto,
            cancellationToken
        );

        Vector embeddingConsulta = CriarVector(valores);

        await using var db = await _dbFactory.CreateDbContextAsync(
            cancellationToken
        );

        var resultados = await db.HashDefaults
            .AsNoTracking()
            .OrderBy(
                x => x.Embedding.CosineDistance(embeddingConsulta)
            )
            .Take(quantidade)
            .Select(x => new
            {
                x.Id,
                x.Description,
                x.DestinationFolder,
                Distance = x.Embedding.CosineDistance(
                    embeddingConsulta
                )
            })
            .ToListAsync(cancellationToken);

        return resultados
            .Select(x => new HashDefaultSimilarityResult(
                x.Id,
                x.Description,
                x.DestinationFolder,
                x.Distance,
                1.0 - x.Distance
            ))
            .ToList();
    }

    // =========================================================
    // CLASSIFICAR DOCUMENTO UTILIZANDO EMBEDDING PERSISTIDO
    // =========================================================

    public async Task<HashDefaultSimilarityResult?> ClassificarDocumentoAsync(
        long documentId,
        double similaridadeMinima,
        CancellationToken cancellationToken = default)
    {
        if (similaridadeMinima < -1.0 ||
            similaridadeMinima > 1.0 ||
            !double.IsFinite(similaridadeMinima))
        {
            throw new ArgumentOutOfRangeException(
                nameof(similaridadeMinima),
                "A similaridade mínima deve estar entre -1 e 1."
            );
        }

        await using var db = await _dbFactory.CreateDbContextAsync(
            cancellationToken
        );

        // 1. Recupera o documento.
        var documento = await db.ArchiveHashCategorizations
            .FirstOrDefaultAsync(
                x => x.Id == documentId,
                cancellationToken
            );

        if (documento is null)
        {
            throw new KeyNotFoundException(
                $"Documento com ID {documentId} não encontrado."
            );
        }

        // 2. Verifica se existe embedding.
        if (documento.Embedding is null)
        {
            throw new InvalidOperationException(
                "O documento não possui embedding persistido."
            );
        }

        // 3. Localiza a categoria mais próxima.
        var melhorCategoria = await db.HashDefaults
            .AsNoTracking()
            .OrderBy(
                x => x.Embedding.CosineDistance(documento.Embedding)
            )
            .Select(x => new
            {
                x.Id,
                x.Description,
                x.DestinationFolder,
                Distance = x.Embedding.CosineDistance(documento.Embedding)
            })
            .FirstOrDefaultAsync(cancellationToken);

        // 4. Nenhuma categoria encontrada.
        if (melhorCategoria is null)
        {
            documento.IdHashDefault = null;
            documento.SimilarityScore = null;
            documento.ClassificationStatus = "unclassified";
            documento.DestinationFolder = null;
            documento.ClassifiedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return null;
        }

        // 5. Calcula a similaridade cosseno.
        double similarity = 1.0 - melhorCategoria.Distance;

        // 6. Registra a melhor categoria encontrada.
        documento.IdHashDefault = melhorCategoria.Id;
        documento.SimilarityScore = similarity;
        documento.ClassifiedAt = DateTimeOffset.UtcNow;

        // 7. Define o status conforme o limiar.
        bool aprovado = similarity >= similaridadeMinima;

        documento.ClassificationStatus = aprovado
            ? "classified"
            : "review";

        // Só registra o destino como aprovado quando a classificação
        // ultrapassa o limiar definido.
        documento.DestinationFolder = aprovado
            ? melhorCategoria.DestinationFolder
            : null;

        await db.SaveChangesAsync(cancellationToken);

        return new HashDefaultSimilarityResult(
            melhorCategoria.Id,
            melhorCategoria.Description,
            melhorCategoria.DestinationFolder,
            melhorCategoria.Distance,
            similarity
        );
    }
}
