using System.Security.Cryptography;
using System.Text;
using FileSyncX.EfCore.Models;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Services;

/// <summary>
/// Processa um PDF, procura uma categoria existente e, quando nenhuma atinge
/// a similaridade mínima, cria uma nova categoria com auxílio da IA.
/// Organiza o arquivo em T:\Documentos_e_Escritorio\pdf\Organizados.
/// </summary>
public sealed class PdfSemanticProcessingService : IPdfSemanticProcessingService
{
    private const string OrganizedRootFolder = @"T:\Documentos_e_Escritorio\pdf\Organizados";

    private readonly IPdfTextExtractionService _textExtractionService;
    private readonly IVecktorVaultService _vecktorVaultService;
    private readonly IAiCategoryDescriptionService _aiCategoryDescriptionService;
    private readonly ISemanticCategoryManagementService _categoryManagementService;

    public PdfSemanticProcessingService(
        IPdfTextExtractionService textExtractionService,
        IVecktorVaultService vecktorVaultService,
        IAiCategoryDescriptionService aiCategoryDescriptionService,
        ISemanticCategoryManagementService categoryManagementService)
    {
        _textExtractionService = textExtractionService
            ?? throw new ArgumentNullException(nameof(textExtractionService));

        _vecktorVaultService = vecktorVaultService
            ?? throw new ArgumentNullException(nameof(vecktorVaultService));

        _aiCategoryDescriptionService = aiCategoryDescriptionService
            ?? throw new ArgumentNullException(nameof(aiCategoryDescriptionService));

        _categoryManagementService = categoryManagementService
            ?? throw new ArgumentNullException(nameof(categoryManagementService));
    }

    public async Task<PdfSemanticProcessingResult> ProcessarPdfAsync(
        string caminhoPdf,
        double similaridadeMinima = 0.75,
        bool moverSeClassificado = true,
        CancellationToken cancellationToken = default)
    {
        ValidarParametros(caminhoPdf, similaridadeMinima);
        cancellationToken.ThrowIfCancellationRequested();

        string caminhoCompleto = Path.GetFullPath(caminhoPdf);
        if (!File.Exists(caminhoCompleto))
            throw new FileNotFoundException("O arquivo PDF não foi encontrado.", caminhoCompleto);

        if (!string.Equals(Path.GetExtension(caminhoCompleto), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O arquivo informado não possui extensão PDF.");

        string sha256 = await CalcularSha256Async(caminhoCompleto, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        long documentId;
        bool alreadyIndexed = false;
        PdfTextExtractionResult? extractionResult = null;
        ArchiveHashCategorization? existingDocument = null;

        long? existingDocumentId = await _vecktorVaultService.BuscarDocumentoPorSha256Async(
            sha256,
            cancellationToken);

        if (existingDocumentId.HasValue)
        {
            documentId = existingDocumentId.Value;
            alreadyIndexed = true;
            existingDocument = await _vecktorVaultService.ObterDocumentoAsync(
                documentId,
                cancellationToken);
        }
        else
        {
            extractionResult = await _textExtractionService.ExtrairTextoAsync(
                caminhoCompleto,
                cancellationToken);

            ValidarTextoExtraido(extractionResult.Text);
            cancellationToken.ThrowIfCancellationRequested();

            documentId = await _vecktorVaultService.IndexarDocumentoAsync(
                sourceFile: caminhoCompleto,
                textoExtraido: extractionResult.Text,
                sha256: sha256,
                cancellationToken: cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Reprocessamentos idênticos preservam uma categoria já atribuída.
        // Isso evita criar repetidamente novas categorias quando a categoria foi
        // criada automaticamente para aquele mesmo documento e seu score interno
        // ainda é inferior ao limiar usado para comparar categorias existentes.
        SemanticCategoryListItem? retainedCategory = null;
        if (alreadyIndexed &&
            existingDocument?.IdHashDefault is int retainedCategoryId &&
            string.Equals(existingDocument.ClassificationStatus, "classified", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<SemanticCategoryListItem> categories =
                await _categoryManagementService.ListAsync(cancellationToken);

            retainedCategory = categories.FirstOrDefault(x => x.Id == retainedCategoryId);
        }

        HashDefaultSimilarityResult? bestExistingCategory = null;
        bool foundExistingCategory;

        if (retainedCategory is not null)
        {
            foundExistingCategory = true;
        }
        else
        {
            // Primeira etapa: verificar categorias existentes usando exatamente
            // o parâmetro de similaridade escolhido na tela de processamento.
            bestExistingCategory = await _vecktorVaultService.ClassificarDocumentoAsync(
                documentId,
                similaridadeMinima,
                cancellationToken);

            foundExistingCategory = bestExistingCategory is not null &&
                                    bestExistingCategory.Similarity >= similaridadeMinima;
        }

        int categoryId;
        string categoryName;
        string destinationFolder;
        string? destinationFile = null;
        bool moved = false;
        double? finalSimilarity;
        string finalCategoryLabel;

        if (foundExistingCategory)
        {
            // A categoria existente atingiu o limiar ou já havia sido atribuída
            // anteriormente a este mesmo documento.
            if (retainedCategory is not null)
            {
                categoryId = retainedCategory.Id;
                categoryName = retainedCategory.CategoryName;
            }
            else
            {
                categoryId = bestExistingCategory!.Id;
                categoryName = ExtrairNomeCategoria(bestExistingCategory.Description);
            }

            destinationFolder = CriarCaminhoPastaCategoria(categoryName);
            finalCategoryLabel = categoryName;
        }
        else
        {
            // Nenhuma categoria existente atingiu o limiar. Só agora executa
            // OCR/extração (se ainda não foi feita) e solicita uma nova categoria.
            if (extractionResult is null)
            {
                extractionResult = await _textExtractionService.ExtrairTextoAsync(
                    caminhoCompleto,
                    cancellationToken);
            }

            ValidarTextoExtraido(extractionResult.Text);
            cancellationToken.ThrowIfCancellationRequested();

            AiCategoryDraft draft = await _aiCategoryDescriptionService.GenerateFromOcrAsync(
                Path.GetFileName(caminhoCompleto),
                extractionResult.Text,
                cancellationToken);

            categoryName = string.IsNullOrWhiteSpace(draft.CategoryName)
                ? "Categoria sem nome"
                : draft.CategoryName.Trim();

            destinationFolder = CriarCaminhoPastaCategoria(categoryName);
            Directory.CreateDirectory(destinationFolder);

            // SaveAsync gera o embedding da descrição canônica e grava a categoria.
            // O serviço também evita duplicidade exata em reprocessamentos.
            categoryId = await _categoryManagementService.SaveAsync(
                categoryId: null,
                categoryName: categoryName,
                semanticDescription: draft.SemanticDescription,
                keywords: draft.Keywords,
                destinationFolder: destinationFolder,
                cancellationToken: cancellationToken);

            finalCategoryLabel = $"{categoryName} (nova categoria)";
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationFolder);

        existingDocument ??= await _vecktorVaultService.ObterDocumentoAsync(
            documentId,
            cancellationToken);

        if (moverSeClassificado)
        {
            (destinationFile, moved) = MoverArquivoParaPasta(
                caminhoCompleto,
                destinationFolder);
        }
        else
        {
            // Mantém o caminho anteriormente registrado, se houver.
            destinationFile = existingDocument?.DestinationFile;
        }

        try
        {
            // Esta associação atualiza o caminho da categoria, o vínculo do
            // documento e a similaridade cosseno real, sem gerar outro embedding.
            HashDefaultSimilarityResult assigned =
                await _categoryManagementService.AssociateDocumentToCategoryAsync(
                    documentId,
                    categoryId,
                    destinationFolder,
                    destinationFile,
                    cancellationToken);

            finalSimilarity = assigned.Similarity;
        }
        catch
        {
            // Se o banco falhar depois da movimentação física, tenta devolver
            // o PDF à origem para não deixar o arquivo fora de sincronia.
            if (moved && destinationFile is not null)
                TentarRestaurarArquivo(destinationFile, caminhoCompleto);

            throw;
        }

        return new PdfSemanticProcessingResult(
            DocumentId: documentId,
            SourceFile: caminhoCompleto,
            Sha256: sha256,
            AlreadyIndexed: alreadyIndexed,
            PageCount: extractionResult?.PageCount ?? 0,
            ExtractedCharacters: extractionResult?.Text.Length ?? 0,
            UsedOcr: extractionResult?.UsedOcr ?? false,
            ClassificationStatus: "classified",
            Similarity: finalSimilarity,
            Category: finalCategoryLabel,
            SuggestedDestinationFolder: destinationFolder,
            Moved: moved,
            DestinationFile: destinationFile);
    }

    private static string CriarCaminhoPastaCategoria(string categoryName)
    {
        string safeName = NormalizarNomePasta(categoryName);
        return Path.Combine(OrganizedRootFolder, safeName);
    }

    private static string NormalizarNomePasta(string value)
    {
        string name = (value ?? string.Empty).Trim();
        if (name.Length == 0)
            name = "Categoria sem nome";

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars())
        {
            '<', '>', ':', '"', '/', '\\', '|', '?', '*'
        };

        var builder = new StringBuilder(name.Length);
        foreach (char character in name)
        {
            if (char.IsControl(character))
                continue;

            builder.Append(invalid.Contains(character) ? ' ' : character);
        }

        name = string.Join(' ', builder.ToString().Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0)
            name = "Categoria sem nome";

        if (name.Length > 90)
            name = name[..90].TrimEnd('.', ' ');

        string reservedBaseName = name.Split('.', 2)[0].Trim();
        string[] reservedNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        if (reservedNames.Contains(reservedBaseName, StringComparer.OrdinalIgnoreCase))
            name = $"_{name}";

        return name;
    }

    private static string ExtrairNomeCategoria(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "Categoria sem nome";

        string firstLine = description
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0)
            ?? "Categoria sem nome";

        const string prefix = "Categoria:";
        if (firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            firstLine = firstLine[prefix.Length..].Trim();

        // Compatibilidade com categorias antigas que armazenavam somente texto corrido.
        if (firstLine.Length > 90)
        {
            int sentenceEnd = firstLine.IndexOfAny(new[] { '.', ';', ':' });
            if (sentenceEnd >= 8 && sentenceEnd <= 90)
                firstLine = firstLine[..sentenceEnd].Trim();
            else
                firstLine = firstLine[..90].Trim();
        }

        return string.IsNullOrWhiteSpace(firstLine)
            ? "Categoria sem nome"
            : firstLine;
    }

    private static (string? DestinationFile, bool Moved) MoverArquivoParaPasta(
        string sourceFile,
        string destinationFolder)
    {
        Directory.CreateDirectory(destinationFolder);

        string sourceDirectory = Path.GetDirectoryName(sourceFile)
            ?? throw new InvalidOperationException("Não foi possível determinar a pasta de origem do PDF.");

        if (string.Equals(
            Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(destinationFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
        {
            return (sourceFile, false);
        }

        string destinationFile = ObterCaminhoDestinoDisponivel(sourceFile, destinationFolder);
        MoverArquivoFisicamente(sourceFile, destinationFile);
        return (destinationFile, true);
    }

    private static string ObterCaminhoDestinoDisponivel(
        string sourceFile,
        string destinationFolder)
    {
        string fileName = Path.GetFileName(sourceFile);
        string nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        string extension = Path.GetExtension(fileName);

        string destination = Path.Combine(destinationFolder, fileName);
        int counter = 1;

        while (File.Exists(destination) || Directory.Exists(destination))
        {
            destination = Path.Combine(
                destinationFolder,
                $"{nameWithoutExtension} ({counter}){extension}");
            counter++;
        }

        return destination;
    }


    private static void MoverArquivoFisicamente(string sourceFile, string destinationFile)
    {
        try
        {
            File.Move(sourceFile, destinationFile);
        }
        catch (IOException) when (File.Exists(sourceFile) && !File.Exists(destinationFile))
        {
            // Permite organizar PDFs cuja origem e destino estejam em volumes diferentes.
            File.Copy(sourceFile, destinationFile, overwrite: false);
            try
            {
                File.Delete(sourceFile);
            }
            catch
            {
                // Se a exclusão da origem falhar, remove a cópia para evitar duplicidade
                // e preserva a exceção que será tratada pelo chamador.
                try
                {
                    if (File.Exists(destinationFile))
                        File.Delete(destinationFile);
                }
                catch
                {
                    // Preserva a exceção original.
                }
                throw;
            }
        }
    }

    private static void TentarRestaurarArquivo(string destinationFile, string sourceFile)
    {
        try
        {
            if (File.Exists(destinationFile) && !File.Exists(sourceFile))
            {
                string sourceDirectory = Path.GetDirectoryName(sourceFile)!;
                Directory.CreateDirectory(sourceDirectory);
                MoverArquivoFisicamente(destinationFile, sourceFile);
            }
        }
        catch
        {
            // Preserva a exceção original do banco de dados.
        }
    }

    private static void ValidarTextoExtraido(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("Não foi possível extrair texto do documento PDF.");
    }

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

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ValidarParametros(string caminhoPdf, double similaridadeMinima)
    {
        if (string.IsNullOrWhiteSpace(caminhoPdf))
            throw new ArgumentException("O caminho do PDF deve ser informado.", nameof(caminhoPdf));

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
