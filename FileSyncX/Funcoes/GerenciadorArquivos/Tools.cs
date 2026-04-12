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
using FileSyncX.ViewModels;
using System.Text.RegularExpressions;

namespace FileSyncX.Funcoes.GerenciadorArquivos;

public class Tools
{
    public static async Task AgruparPdfsPorPrefixoAsync(string pastaRaiz, IEnumerable<ArquivoModel> arquivos, Func<bool> checkPaused, Action<string, bool, string>? reportarProgresso = null)
    {
        var arquivosPdf = arquivos.Where(a => a.Extensao.ToLower() == ".pdf").ToList();
        if (arquivosPdf.Count < 2) return;

        reportarProgresso?.Invoke(string.Empty, true, "Analisando prefixos para mesclagem...");

        // Regex para capturar tudo antes do último separador como PREFIXO e os dígitos finais como SEQUÊNCIA
        // Exemplo: IMG_20220405_0001.pdf -> Prefixo: "IMG_20220405", Seq: "0001"
        var regex = new Regex(@"^(.*)[_\-\s]+(\d+)\.pdf$", RegexOptions.IgnoreCase);

        // Dicionário para agrupar os arquivos: Chave = Prefixo, Valor = Lista de (Arquivo, NumSequencia)
        var grupos = new Dictionary<string, List<(ArquivoModel Arquivo, int Sequencia)>>();

        foreach (var pdf in arquivosPdf)
        {
            var match = regex.Match(pdf.Nome);
            if (match.Success)
            {
                string prefixo = match.Groups[1].Value.Trim();
                if (int.TryParse(match.Groups[2].Value, out int sequencia))
                {
                    if (!grupos.ContainsKey(prefixo))
                        grupos[prefixo] = new List<(ArquivoModel, int)>();

                    grupos[prefixo].Add((pdf, sequencia));
                }
            }
        }

        // Filtramos apenas os grupos que têm mais de 1 arquivo (senão não há o que mesclar)
        var gruposParaMesclar = grupos.Where(g => g.Value.Count > 1).ToList();

        if (gruposParaMesclar.Count == 0)
        {
            reportarProgresso?.Invoke(string.Empty, false, "Nenhum conjunto de PDFs sequenciais encontrado.");
            return;
        }

        foreach (var grupo in gruposParaMesclar)
        {
            if (checkPaused != null && checkPaused())
            {
                reportarProgresso?.Invoke(string.Empty, false, "PAUSADO.");
                while (checkPaused()) { await Task.Delay(500); }
            }

            string prefixo = grupo.Key;

            // Ordena a lista de arquivos pelo número sequencial para as páginas não ficarem fora de ordem
            var arquivosOrdenados = grupo.Value.OrderBy(x => x.Sequencia).ToList();

            string nomeNovoPdf = $"{prefixo}_Agrupado.pdf";
            string caminhoNovoPdf = Path.Combine(pastaRaiz, nomeNovoPdf);

            // Garante que não vai sobrescrever um arquivo existente
            int cont = 1;
            while (File.Exists(caminhoNovoPdf))
            {
                caminhoNovoPdf = Path.Combine(pastaRaiz, $"{prefixo}_Agrupado ({cont}).pdf");
                cont++;
            }

            reportarProgresso?.Invoke(caminhoNovoPdf, true, $"Mesclando {arquivosOrdenados.Count} páginas...");

            bool sucesso = false;
            try
            {
                var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();

                foreach (var item in arquivosOrdenados)
                {
                    // Lemos o arquivo da RAM (se existir) ou direto do disco
                    byte[] bytesLidos = item.Arquivo.Conteudo ?? File.ReadAllBytes(item.Arquivo.CaminhoCompleto);

                    using (var streamOriginal = new MemoryStream(bytesLidos))
                    using (var pdfOriginal = UglyToad.PdfPig.PdfDocument.Open(streamOriginal))
                    {
                        // Copia as páginas do arquivo solto para o documento agrupado
                        for (int i = 1; i <= pdfOriginal.NumberOfPages; i++)
                        {
                            builder.AddPage(pdfOriginal, i);
                        }
                    }
                }

                byte[] pdfMescladoBytes = builder.Build();
                File.WriteAllBytes(caminhoNovoPdf, pdfMescladoBytes);
                sucesso = true;
            }
            catch (Exception ex)
            {
                reportarProgresso?.Invoke(caminhoNovoPdf, false, $"Erro ao mesclar: {ex.Message}");
                continue;
            }

            if (sucesso)
            {
                // Deleta as páginas soltas originais após a mesclagem bem sucedida
                foreach (var item in arquivosOrdenados)
                {
                    try
                    {
                        if (File.Exists(item.Arquivo.CaminhoCompleto))
                        {
                            File.Delete(item.Arquivo.CaminhoCompleto);
                        }
                        reportarProgresso?.Invoke(item.Arquivo.CaminhoCompleto, false, "Removido (Mesclado)");
                    }
                    catch { }
                }

                reportarProgresso?.Invoke(caminhoNovoPdf, false, "Conjunto mesclado com sucesso!");
            }
        }
    }

}