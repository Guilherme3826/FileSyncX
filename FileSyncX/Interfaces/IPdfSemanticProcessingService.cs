using FileSyncX.Repositories.Results;

namespace FileSyncX.Interfaces;

public interface IPdfSemanticProcessingService
{
    Task<PdfSemanticProcessingResult> ProcessarPdfAsync(
        string caminhoPdf,
        double similaridadeMinima = 0.75,
        bool moverSeClassificado = true,
        CancellationToken cancellationToken = default);
}
