using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;

// Os usings foram mantidos apenas para tipos que não geram conflito.
// Tipos ambíguos estão com namespace completo no código abaixo.
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace FileSyncX.Funcoes.GerenciadorArquivos;

public static class ProcessadorOcrPdf
{
    public static async Task InjetarOcrEmPdfsAsync(string pastaRaiz, Func<bool> checkPaused, Action<string, string>? reportarProgresso = null)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz))
        {
            reportarProgresso?.Invoke(pastaRaiz, "Pasta raiz não encontrada ou inválida.");
            return;
        }

        OcrEngine engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("pt-BR"));
        if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();

        if (engine == null)
        {
            reportarProgresso?.Invoke(string.Empty, "ERRO CRÍTICO: Não foi possível inicializar o OCR do Windows.");
            return;
        }

        var arquivosPdf = Directory.GetFiles(pastaRaiz, "*.pdf", SearchOption.AllDirectories)
                                   .Where(f => !f.EndsWith("_OCR.pdf", StringComparison.OrdinalIgnoreCase))
                                   .ToArray();

        if (arquivosPdf.Length == 0)
        {
            reportarProgresso?.Invoke(string.Empty, "Nenhum arquivo PDF (sem OCR) encontrado na pasta especificada.");
            return;
        }

        reportarProgresso?.Invoke(string.Empty, $"Iniciando processamento com renderização nativa: {arquivosPdf.Length} PDFs...");

        foreach (var caminhoPdf in arquivosPdf)
        {
            if (checkPaused != null && checkPaused())
            {
                reportarProgresso?.Invoke(caminhoPdf, "PAUSADO.");
                while (checkPaused()) { await Task.Delay(500); }
                reportarProgresso?.Invoke(caminhoPdf, "Retomando...");
            }

            bool precisaOcr = false;
            StringBuilder textoTotal = new StringBuilder();

            reportarProgresso?.Invoke(caminhoPdf, "Verificando texto nativo...");

            try
            {
                using (var stream = File.OpenRead(caminhoPdf))
                // QUALIFICADO: UglyToad.PdfPig.PdfDocument
                using (var pdf = UglyToad.PdfPig.PdfDocument.Open(stream))
                {
                    foreach (var page in pdf.GetPages())
                    {
                        textoTotal.Append(page.Text).Append(" ");
                    }
                }

                if (textoTotal.Length < 50)
                {
                    precisaOcr = true;
                }
            }
            catch (Exception ex)
            {
                reportarProgresso?.Invoke(caminhoPdf, $"Erro ao ler o PDF: {ex.Message}");
                continue;
            }

            if (precisaOcr)
            {
                reportarProgresso?.Invoke(caminhoPdf, "Rasterizando página e aplicando OCR...");

                string novoCaminhoPdf = Path.Combine(
                    Path.GetDirectoryName(caminhoPdf) ?? string.Empty,
                    Path.GetFileNameWithoutExtension(caminhoPdf) + "_OCR.pdf"
                );

                bool sucessoNaInjecao = false;
                StringBuilder textoReconhecidoTotal = new StringBuilder();

                try
                {
                    byte[] fileBytes = File.ReadAllBytes(caminhoPdf);

                    using (var ras = new InMemoryRandomAccessStream())
                    {
                        using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
                        {
                            writer.WriteBytes(fileBytes);
                            await writer.StoreAsync();
                        }

                        // QUALIFICADO: Windows.Data.Pdf.PdfDocument
                        var winPdf = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(ras);

                        for (uint i = 0; i < winPdf.PageCount; i++)
                        {
                            using (var winPage = winPdf.GetPage(i))
                            using (var pageStream = new InMemoryRandomAccessStream())
                            {
                                // QUALIFICADO: Windows.Data.Pdf.PdfPageRenderOptions
                                var renderOptions = new Windows.Data.Pdf.PdfPageRenderOptions
                                {
                                    DestinationWidth = (uint)(winPage.Size.Width * 2)
                                };

                                await winPage.RenderToStreamAsync(pageStream, renderOptions);

                                var decoder = await BitmapDecoder.CreateAsync(pageStream);
                                var sb = await decoder.GetSoftwareBitmapAsync();
                                var ocrResult = await engine.RecognizeAsync(sb);

                                if (ocrResult != null && ocrResult.Lines != null)
                                {
                                    foreach (var line in ocrResult.Lines)
                                    {
                                        textoReconhecidoTotal.AppendLine(line.Text);
                                    }
                                }
                            }
                        }
                    }

                    string textoFinal = SanitizarParaAsciiPuro(textoReconhecidoTotal.ToString());

                    if (!string.IsNullOrWhiteSpace(textoFinal))
                    {
                        reportarProgresso?.Invoke(caminhoPdf, "Gerando cópia segura com página de transcrição...");

                        byte[] pdfFinalBytes;

                        // QUALIFICADO: UglyToad.PdfPig.Writer.PdfDocumentBuilder
                        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
                        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);

                        using (var streamOriginal = new MemoryStream(fileBytes))
                        // QUALIFICADO: UglyToad.PdfPig.PdfDocument
                        using (var pdfOriginal = UglyToad.PdfPig.PdfDocument.Open(streamOriginal))
                        {
                            for (int i = 1; i <= pdfOriginal.NumberOfPages; i++)
                            {
                                builder.AddPage(pdfOriginal, i);
                            }

                            // QUALIFICADO: UglyToad.PdfPig.Content.PageSize
                            var paginaTexto = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);

                            decimal fontSize = 10m;
                            double lineSpacing = 14;
                            double margin = 30;
                            double yPos = paginaTexto.PageSize.Height - margin;

                            // QUALIFICADO: UglyToad.PdfPig.Core.PdfPoint
                            paginaTexto.AddText("--- TRANSCRICAO OCR PARA COPIA ---", 12m, new UglyToad.PdfPig.Core.PdfPoint(margin, yPos), font);
                            yPos -= lineSpacing * 2;

                            var linhasDeTexto = textoFinal.Split('\n');

                            foreach (var linha in linhasDeTexto)
                            {
                                if (string.IsNullOrWhiteSpace(linha)) continue;

                                if (yPos < margin)
                                {
                                    // QUALIFICADO: UglyToad.PdfPig.Content.PageSize
                                    paginaTexto = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
                                    yPos = paginaTexto.PageSize.Height - margin;
                                }

                                // QUALIFICADO: UglyToad.PdfPig.Core.PdfPoint
                                paginaTexto.AddText(linha.Trim(), fontSize, new UglyToad.PdfPig.Core.PdfPoint(margin, yPos), font);
                                yPos -= lineSpacing;
                            }

                            pdfFinalBytes = builder.Build();
                        }

                        File.WriteAllBytes(novoCaminhoPdf, pdfFinalBytes);
                        sucessoNaInjecao = true;
                    }
                }
                catch (Exception ex)
                {
                    reportarProgresso?.Invoke(caminhoPdf, $"Erro durante o OCR/Criação do PDF: {ex.Message}");
                    if (File.Exists(novoCaminhoPdf)) File.Delete(novoCaminhoPdf);
                    continue;
                }

                if (sucessoNaInjecao && File.Exists(novoCaminhoPdf))
                {
                    reportarProgresso?.Invoke(caminhoPdf, "Cópia criada com sucesso (*_OCR.pdf).");
                }
                else
                {
                    reportarProgresso?.Invoke(caminhoPdf, "Processamento finalizado, mas nenhum texto útil foi extraído.");
                }
            }
            else
            {
                reportarProgresso?.Invoke(caminhoPdf, "Ignorado (Já possui texto nativo).");
            }
        }

        reportarProgresso?.Invoke(string.Empty, "Processamento de OCR em lote concluído!");
    }

    private static string SanitizarParaAsciiPuro(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        string textoLimpo = texto.Replace("\r", "").Replace("\t", " ");

        var textoNormalizado = textoLimpo.Normalize(NormalizationForm.FormD);
        var construtor = new StringBuilder();

        foreach (var c in textoNormalizado)
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);

            if (categoria != UnicodeCategory.NonSpacingMark)
            {
                if (c >= 32 && c <= 126)
                {
                    construtor.Append(c);
                }
                else if (c == '\n')
                {
                    construtor.Append(c);
                }
            }
        }

        return construtor.ToString();
    }
}