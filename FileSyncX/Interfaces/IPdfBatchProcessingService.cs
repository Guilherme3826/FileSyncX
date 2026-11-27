using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface IPdfBatchProcessingService
{
    Task<PdfBatchProcessingResult> ProcessarPastaAsync(
        string caminhoPasta,
        bool incluirSubpastas = true,
        double similaridadeMinima = 0.75,
        bool moverSeClassificado = false,
        IProgress<PdfBatchProgress>? progresso = null,
        CancellationToken cancellationToken = default);
}