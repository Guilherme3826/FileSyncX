using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

namespace FileSyncX.Services;

public sealed class PdfBatchProcessingService : IPdfBatchProcessingService
{
    private readonly IPdfSemanticProcessingService _pdfSemanticProcessingService;

    public PdfBatchProcessingService(
        IPdfSemanticProcessingService pdfSemanticProcessingService)
    {
        _pdfSemanticProcessingService =
            pdfSemanticProcessingService
            ?? throw new ArgumentNullException(
                nameof(pdfSemanticProcessingService));
    }

    public async Task<PdfBatchProcessingResult> ProcessarPastaAsync(
        string caminhoPasta,
        bool incluirSubpastas = true,
        double similaridadeMinima = 0.75,
        bool moverSeClassificado = true,
        IProgress<PdfBatchProgress>? progresso = null,
        CancellationToken cancellationToken = default)
    {
        ValidarParametros(
            caminhoPasta,
            similaridadeMinima);

        string pastaRaiz = Path.GetFullPath(caminhoPasta);

        if (!Directory.Exists(pastaRaiz))
        {
            throw new DirectoryNotFoundException(
                $"A pasta informada não existe: {pastaRaiz}");
        }

        progresso?.Report(new PdfBatchProgress(
            0,
            0,
            null,
            "Localizando arquivos PDF..."));

        // A enumeração é executada fora da thread da interface.
        // O cancelamento é verificado durante a própria enumeração.
        EnumeracaoPdfResult enumeracao = await Task.Run(
            () => EnumerarArquivosPdf(
                pastaRaiz,
                incluirSubpastas,
                cancellationToken),
            CancellationToken.None);

        List<string> arquivos = enumeracao.Arquivos;

        if (enumeracao.FoiCancelada)
        {
            progresso?.Report(new PdfBatchProgress(
                arquivos.Count,
                0,
                null,
                "Enumeração cancelada."));

            return CriarResultado(
                pastaRaiz,
                arquivos.Count,
                [],
                enumeracao.Avisos,
                wasCancelled: true);
        }

        int total = arquivos.Count;

        progresso?.Report(new PdfBatchProgress(
            total,
            0,
            null,
            total == 0
                ? "Nenhum PDF encontrado."
                : $"{total} arquivo(s) PDF encontrado(s)."));

        var resultados = new List<PdfBatchFileResult>(total);
        bool cancelado = false;

        for (int i = 0; i < total; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelado = true;
                break;
            }

            string arquivo = arquivos[i];
            int numeroArquivo = i + 1;

            progresso?.Report(new PdfBatchProgress(
                total,
                resultados.Count,
                arquivo,
                $"Processando {numeroArquivo} de {total}: {Path.GetFileName(arquivo)}"));

            try
            {
                var resultado =
                    await _pdfSemanticProcessingService.ProcessarPdfAsync(
                        caminhoPdf: arquivo,
                        similaridadeMinima: similaridadeMinima,
                        moverSeClassificado: moverSeClassificado,
                        cancellationToken: cancellationToken);

                resultados.Add(new PdfBatchFileResult(
                    SourceFile: arquivo,
                    Succeeded: true,
                    AlreadyIndexed: resultado.AlreadyIndexed,
                    ClassificationStatus: resultado.ClassificationStatus,
                    Similarity: resultado.Similarity,
                    Category: resultado.Category,
                    SuggestedDestinationFolder:
                        resultado.SuggestedDestinationFolder,
                    Moved: resultado.Moved,
                    DestinationFile: resultado.DestinationFile,
                    ErrorMessage: null));
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                cancelado = true;
                break;
            }
            catch (Exception ex)
            {
                // Um arquivo com problema não interrompe os demais.
                resultados.Add(new PdfBatchFileResult(
                    SourceFile: arquivo,
                    Succeeded: false,
                    AlreadyIndexed: false,
                    ClassificationStatus: null,
                    Similarity: null,
                    Category: null,
                    SuggestedDestinationFolder: null,
                    Moved: false,
                    DestinationFile: null,
                    ErrorMessage: ex.GetBaseException().Message));
            }

            progresso?.Report(new PdfBatchProgress(
                total,
                resultados.Count,
                arquivo,
                $"Concluído: {resultados.Count} de {total}."));
        }

        var resultadoFinal = CriarResultado(
            pastaRaiz,
            total,
            resultados,
            enumeracao.Avisos,
            cancelado);

        progresso?.Report(new PdfBatchProgress(
            total,
            resultadoFinal.Processed,
            null,
            cancelado
                ? "Processamento cancelado."
                : "Processamento do lote concluído."));

        return resultadoFinal;
    }

    private static void ValidarParametros(
        string caminhoPasta,
        double similaridadeMinima)
    {
        if (string.IsNullOrWhiteSpace(caminhoPasta))
        {
            throw new ArgumentException(
                "O caminho da pasta deve ser informado.",
                nameof(caminhoPasta));
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

    private static EnumeracaoPdfResult EnumerarArquivosPdf(
        string pastaRaiz,
        bool incluirSubpastas,
        CancellationToken cancellationToken)
    {
        var arquivos = new List<string>();
        var avisos = new List<PdfBatchFolderWarning>();
        var pastasPendentes = new Stack<string>();

        pastasPendentes.Push(pastaRaiz);

        bool foiCancelada = false;

        while (pastasPendentes.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                foiCancelada = true;
                break;
            }

            string pastaAtual = pastasPendentes.Pop();

            // Arquivos da pasta atual.
            try
            {
                foreach (string arquivo in
                    Directory.EnumerateFiles(
                        pastaAtual,
                        "*",
                        SearchOption.TopDirectoryOnly))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        foiCancelada = true;
                        break;
                    }

                    if (string.Equals(
                        Path.GetExtension(arquivo),
                        ".pdf",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        arquivos.Add(Path.GetFullPath(arquivo));
                    }
                }
            }
            catch (Exception ex) when (EhErroDeAcessoOuIO(ex))
            {
                avisos.Add(new PdfBatchFolderWarning(
                    pastaAtual,
                    ex.GetBaseException().Message));
            }

            if (foiCancelada || !incluirSubpastas)
            {
                if (foiCancelada)
                    break;

                continue;
            }

            // Subpastas da pasta atual.
            try
            {
                var subpastas = Directory
                    .EnumerateDirectories(
                        pastaAtual,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .OrderByDescending(
                        p => p,
                        StringComparer.OrdinalIgnoreCase);

                foreach (string subpasta in subpastas)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        foiCancelada = true;
                        break;
                    }

                    try
                    {
                        var info = new DirectoryInfo(subpasta);

                        // Evita ciclos e entradas em diretórios externos
                        // por meio de links simbólicos ou junções.
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            avisos.Add(new PdfBatchFolderWarning(
                                subpasta,
                                "Pasta ignorada por ser um link simbólico ou junção."));

                            continue;
                        }

                        pastasPendentes.Push(subpasta);
                    }
                    catch (Exception ex) when (EhErroDeAcessoOuIO(ex))
                    {
                        avisos.Add(new PdfBatchFolderWarning(
                            subpasta,
                            ex.GetBaseException().Message));
                    }
                }
            }
            catch (Exception ex) when (EhErroDeAcessoOuIO(ex))
            {
                avisos.Add(new PdfBatchFolderWarning(
                    pastaAtual,
                    ex.GetBaseException().Message));
            }

            if (foiCancelada)
                break;
        }

        arquivos = arquivos
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(
                p => p,
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new EnumeracaoPdfResult(
            arquivos,
            avisos,
            foiCancelada);
    }

    private static bool EhErroDeAcessoOuIO(Exception ex)
    {
        return ex is UnauthorizedAccessException
            or IOException
            or SecurityException;
    }

    private static PdfBatchProcessingResult CriarResultado(
        string pastaRaiz,
        int totalEncontrado,
        IReadOnlyList<PdfBatchFileResult> resultados,
        IReadOnlyList<PdfBatchFolderWarning> avisos,
        bool wasCancelled)
    {
        return new PdfBatchProcessingResult(
            SourceFolder: pastaRaiz,
            TotalFound: totalEncontrado,
            Files: resultados,
            Warnings: avisos,
            WasCancelled: wasCancelled);
    }

    private sealed record EnumeracaoPdfResult(
        List<string> Arquivos,
        List<PdfBatchFolderWarning> Avisos,
        bool FoiCancelada);
}