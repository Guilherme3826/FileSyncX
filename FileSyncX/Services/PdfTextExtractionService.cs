
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using FileSyncX.Interfaces;
using FileSyncX.Repositories.Results;

using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace FileSyncX.Services;

public sealed class PdfTextExtractionService : IPdfTextExtractionService
{
    private const int MinimumNativeCharacters = 80;

    // =========================================================
    // EXTRAIR TEXTO DO PDF
    // =========================================================

    public async Task<PdfTextExtractionResult> ExtrairTextoAsync(
        string caminhoPdf,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caminhoPdf))
        {
            throw new ArgumentException(
                "Caminho do PDF não informado.",
                nameof(caminhoPdf)
            );
        }

        if (!File.Exists(caminhoPdf))
        {
            throw new FileNotFoundException(
                "Arquivo PDF não encontrado.",
                caminhoPdf
            );
        }

        if (!string.Equals(
            Path.GetExtension(caminhoPdf),
            ".pdf",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "O arquivo informado não possui extensão PDF."
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        // =====================================================
        // 1. EXTRAÇÃO NATIVA COM PDFPIG
        // =====================================================

        var textoNativo = new StringBuilder();

        int quantidadePaginas;

        try
        {
            using var stream = File.OpenRead(caminhoPdf);

            using var pdf =
                UglyToad.PdfPig.PdfDocument.Open(stream);

            quantidadePaginas = pdf.NumberOfPages;

            foreach (var pagina in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                textoNativo.AppendLine(pagina.Text);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Não foi possível interpretar o PDF: {ex.Message}",
                ex
            );
        }

        string texto = NormalizarTexto(
            textoNativo.ToString()
        );

        int caracteresUteis = texto.Count(
            c => !char.IsWhiteSpace(c)
        );

        // Se houver texto nativo suficiente, não executa OCR.
        if (caracteresUteis >= MinimumNativeCharacters)
        {
            return new PdfTextExtractionResult(
                texto,
                UsedOcr: false,
                PageCount: quantidadePaginas
            );
        }

        cancellationToken.ThrowIfCancellationRequested();

        // =====================================================
        // 2. INICIALIZAR OCR DO WINDOWS
        // =====================================================

        OcrEngine? engine = OcrEngine.TryCreateFromLanguage(
            new Windows.Globalization.Language("pt-BR")
        );

        engine ??= OcrEngine.TryCreateFromUserProfileLanguages();

        if (engine is null)
        {
            throw new InvalidOperationException(
                "Não foi possível inicializar o OCR do Windows. " +
                "Verifique se o idioma português está instalado."
            );
        }

        // =====================================================
        // 3. CARREGAR PDF PARA A API WINDOWS
        // =====================================================

        byte[] pdfBytes = await File.ReadAllBytesAsync(
            caminhoPdf,
            cancellationToken
        );

        using var pdfStream = new InMemoryRandomAccessStream();

        using (var writer = new DataWriter(
            pdfStream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(pdfBytes);

            await writer.StoreAsync();
            await writer.FlushAsync();
        }

        pdfStream.Seek(0);

        var windowsPdf =
            await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(
                pdfStream
            );

        var textoOcr = new StringBuilder();

        // =====================================================
        // 4. RENDERIZAR PÁGINAS E RECONHECER TEXTO
        // =====================================================

        for (uint i = 0; i < windowsPdf.PageCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var pagina = windowsPdf.GetPage(i);

            using var pageStream =
                new InMemoryRandomAccessStream();

            var renderOptions =
                new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(
                        1,
                        Math.Ceiling(pagina.Size.Width * 2)
                    )
                };

            await pagina.RenderToStreamAsync(
                pageStream,
                renderOptions
            );

            pageStream.Seek(0);

            // BitmapDecoder não implementa IDisposable:
            // não utilizar using nesta declaração.
            var decoder = await BitmapDecoder.CreateAsync(
                pageStream
            );

            using var bitmap =
                await decoder.GetSoftwareBitmapAsync();

            var resultadoOcr = await engine.RecognizeAsync(
                bitmap
            );

            textoOcr.AppendLine();
            textoOcr.AppendLine($"[Página {i + 1}]");

            foreach (var linha in resultadoOcr.Lines)
            {
                textoOcr.AppendLine(linha.Text);
            }
        }

        // =====================================================
        // 5. NORMALIZAR TEXTO RECONHECIDO
        // =====================================================

        string textoFinal = NormalizarTexto(
            textoOcr.ToString()
        );

        if (string.IsNullOrWhiteSpace(textoFinal))
        {
            throw new InvalidOperationException(
                "Nenhum texto foi identificado no PDF, " +
                "mesmo após a tentativa de OCR."
            );
        }

        return new PdfTextExtractionResult(
            textoFinal,
            UsedOcr: true,
            PageCount: (int)windowsPdf.PageCount
        );
    }

    // =========================================================
    // NORMALIZAR TEXTO
    // =========================================================

    private static string NormalizarTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        texto = texto.Replace("\0", " ");

        texto = Regex.Replace(
            texto,
            @"[ \t]+",
            " "
        );

        texto = Regex.Replace(
            texto,
            @"\n{3,}",
            "\n\n"
        );

        return texto.Trim();
    }
}
