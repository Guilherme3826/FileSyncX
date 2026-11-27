using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Services;

public sealed class PdfSemanticProcessingService
    : IPdfSemanticProcessingService
{
    private readonly IPdfTextExtractionService _textExtractionService;
    private readonly IVecktorVaultService _vecktorVaultService;

    public PdfSemanticProcessingService(
        IPdfTextExtractionService textExtractionService,
        IVecktorVaultService vecktorVaultService)
    {
        _textExtractionService =
            textExtractionService
            ?? throw new ArgumentNullException(
                nameof(textExtractionService));

        _vecktorVaultService =
            vecktorVaultService
            ?? throw new ArgumentNullException(
                nameof(vecktorVaultService));
    }

    public async Task<PdfSemanticProcessingResult> ProcessarPdfAsync(
        string caminhoPdf,
        double similaridadeMinima = 0.75,
        bool moverSeClassificado = false,
        CancellationToken cancellationToken = default)
    {
        ValidarParametros(caminhoPdf, similaridadeMinima);

        cancellationToken.ThrowIfCancellationRequested();

        string caminhoCompleto = Path.GetFullPath(caminhoPdf);

        if (!File.Exists(caminhoCompleto))
        {
            throw new FileNotFoundException(
                "O arquivo PDF não foi encontrado.",
                caminhoCompleto);
        }

        if (!string.Equals(
            Path.GetExtension(caminhoCompleto),
            ".pdf",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "O arquivo informado não possui extensão PDF.");
        }

        // ============================================================
        // 1. CALCULAR SHA-256
        // ============================================================

        string sha256 = await CalcularSha256Async(
            caminhoCompleto,
            cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // ============================================================
        // 2. VERIFICAR SE O DOCUMENTO JÁ ESTÁ INDEXADO
        // ============================================================

        long documentId;

        bool alreadyIndexed = false;

        PdfTextExtractionResult? extractionResult = null;

        long? documentoExistente =
            await _vecktorVaultService.BuscarDocumentoPorSha256Async(
                sha256,
                cancellationToken);

        if (documentoExistente.HasValue)
        {
            documentId = documentoExistente.Value;
            alreadyIndexed = true;
        }
        else
        {
            // ========================================================
            // 3. EXTRAIR TEXTO E EXECUTAR OCR QUANDO NECESSÁRIO
            // ========================================================

            extractionResult =
                await _textExtractionService.ExtrairTextoAsync(
                    caminhoCompleto,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(extractionResult.Text))
            {
                throw new InvalidDataException(
                    "Não foi possível extrair texto do documento PDF.");
            }

            // ========================================================
            // 4. GERAR EMBEDDING E INDEXAR NO VECKTORVAULT
            // ========================================================

            documentId =
                await _vecktorVaultService.IndexarDocumentoAsync(
                    sourceFile: caminhoCompleto,
                    textoExtraido: extractionResult.Text,
                    sha256: sha256,
                    cancellationToken: cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // ============================================================
        // 5. CLASSIFICAR DOCUMENTO
        // ============================================================

        HashDefaultSimilarityResult? classification =
            await _vecktorVaultService.ClassificarDocumentoAsync(
                documentId,
                similaridadeMinima,
                cancellationToken);

        // Recuperar o documento atualizado.
        var documento =
            await _vecktorVaultService.ObterDocumentoAsync(
                documentId,
                cancellationToken);

        if (documento is null)
        {
            throw new InvalidOperationException(
                $"O documento de ID {documentId} não foi encontrado no VecktorVault.");
        }

        string status = documento.ClassificationStatus;

        string? destinationFolder = documento.DestinationFolder;

        double? similarity =
            classification?.Similarity
            ?? documento.SimilarityScore;

        string? category = classification?.Description;

        // ============================================================
        // 6. MOVIMENTAR ARQUIVO, SE AUTORIZADO
        // ============================================================

        bool moved = false;

        string? destinationFile = documento.DestinationFile;

        bool podeMover =
            moverSeClassificado &&
            !alreadyIndexed &&
            string.Equals(
                status,
                "classified",
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(destinationFolder);

        if (podeMover)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string pastaDestino =
                Path.GetFullPath(destinationFolder!);

            Directory.CreateDirectory(pastaDestino);

            string caminhoDestino =
                ObterCaminhoDestinoDisponivel(
                    caminhoCompleto,
                    pastaDestino);

            // Se a origem e o destino forem iguais, não executar Move.
            if (string.Equals(
                caminhoCompleto,
                caminhoDestino,
                StringComparison.OrdinalIgnoreCase))
            {
                await _vecktorVaultService.RegistrarDestinoDocumentoAsync(
                    documentId,
                    caminhoCompleto,
                    pastaDestino,
                    cancellationToken);

                destinationFile = caminhoCompleto;
            }
            else
            {
                // Libera completamente o arquivo antes da movimentação.
                File.Move(caminhoCompleto, caminhoDestino);

                try
                {
                    await _vecktorVaultService.RegistrarDestinoDocumentoAsync(
                        documentId,
                        caminhoDestino,
                        pastaDestino,
                        cancellationToken);

                    moved = true;
                    destinationFile = caminhoDestino;
                }
                catch
                {
                    // Tentar restaurar o arquivo caso a atualização
                    // do banco de dados falhe após a movimentação.
                    try
                    {
                        if (File.Exists(caminhoDestino) &&
                            !File.Exists(caminhoCompleto))
                        {
                            File.Move(
                                caminhoDestino,
                                caminhoCompleto);
                        }
                    }
                    catch
                    {
                        // Preservar a exceção original.
                    }

                    throw;
                }
            }
        }

        // ============================================================
        // 7. RETORNAR RESULTADO
        // ============================================================

        return new PdfSemanticProcessingResult(
            DocumentId: documentId,
            SourceFile: caminhoCompleto,
            Sha256: sha256,
            AlreadyIndexed: alreadyIndexed,
            PageCount: extractionResult?.PageCount ?? 0,
            ExtractedCharacters: extractionResult?.Text.Length ?? 0,
            UsedOcr: extractionResult?.UsedOcr ?? false,
            ClassificationStatus: status,
            Similarity: similarity,
            Category: category,
            SuggestedDestinationFolder: destinationFolder,
            Moved: moved,
            DestinationFile: destinationFile);
    }

    // ================================================================
    // SHA-256
    // ================================================================

    private static async Task<string> CalcularSha256Async(
        string caminhoArquivo,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            caminhoArquivo,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ================================================================
    // DESTINO DISPONÍVEL
    // ================================================================

    private static string ObterCaminhoDestinoDisponivel(
        string caminhoOrigem,
        string pastaDestino)
    {
        string nomeArquivo = Path.GetFileName(caminhoOrigem);

        string nomeSemExtensao =
            Path.GetFileNameWithoutExtension(nomeArquivo);

        string extensao = Path.GetExtension(nomeArquivo);

        string destino = Path.Combine(
            pastaDestino,
            nomeArquivo);

        int contador = 1;

        while (File.Exists(destino) || Directory.Exists(destino))
        {
            string novoNome =
                $"{nomeSemExtensao} ({contador}){extensao}";

            destino = Path.Combine(
                pastaDestino,
                novoNome);

            contador++;
        }

        return destino;
    }

    // ================================================================
    // VALIDAÇÃO
    // ================================================================

    private static void ValidarParametros(
        string caminhoPdf,
        double similaridadeMinima)
    {
        if (string.IsNullOrWhiteSpace(caminhoPdf))
        {
            throw new ArgumentException(
                "O caminho do PDF deve ser informado.",
                nameof(caminhoPdf));
        }

        if (double.IsNaN(similaridadeMinima) ||
            double.IsInfinity(similaridadeMinima) ||
            similaridadeMinima < 0 ||
            similaridadeMinima > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(similaridadeMinima),
                "A similaridade mínima deve estar entre 0 e 1.");
        }
    }
}