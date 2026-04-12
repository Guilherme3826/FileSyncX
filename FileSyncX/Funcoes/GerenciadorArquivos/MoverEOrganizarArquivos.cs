// MoverEOrganizarArquivos.cs
using FileSyncX.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

// APIS do Windows 
using Windows.Media.Ocr;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Core;

namespace FileSyncX.Funcoes.GerenciadorArquivos;

public static class MoverEOrganizarArquivos
{
    private static readonly string[] ExtensoesShapefile = new[]
    {
        ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx",
        ".atx", ".fbn", ".fbx", ".qmd", ".ain", ".aih", ".ixs", ".mxs"
    };

    private static ConfigModel LerConfiguracao()
    {
        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        string caminhoJson = Path.Combine(pastaAppData, "extensoes_config.json");
        if (File.Exists(caminhoJson))
        {
            try { return JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(caminhoJson)) ?? new ConfigModel(); }
            catch { }
        }
        return new ConfigModel();
    }

    private static string ObterPastaDestinoBase(string extensao, ConfigModel config)
    {
        string extLimpa = extensao.Replace(".", "").ToLower();
        if (string.IsNullOrWhiteSpace(extLimpa)) extLimpa = "sem_extensao";

        string categoria = "Desconhecidos";
        foreach (var cat in config.Categorias)
        {
            if (cat.Value.ContainsKey(extLimpa))
            {
                categoria = cat.Key;
                break;
            }
        }

        string destinoRaiz = config.PastaDestinoRaiz;
        if (string.IsNullOrWhiteSpace(destinoRaiz)) destinoRaiz = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FileSyncX_Destino");

        return Path.Combine(destinoRaiz, categoria, extLimpa);
    }

    private static string ObterCaminhoComTagNovo(string caminhoOriginal)
    {
        if (string.IsNullOrWhiteSpace(caminhoOriginal) || !File.Exists(caminhoOriginal)) return caminhoOriginal;

        string diretorio = Path.GetDirectoryName(caminhoOriginal) ?? string.Empty;
        string nomeSemExtensao = Path.GetFileNameWithoutExtension(caminhoOriginal);
        string extensao = Path.GetExtension(caminhoOriginal);

        string novoCaminho = Path.Combine(diretorio, $"{nomeSemExtensao}[NOVO]{extensao}");
        int cont = 1;
        while (File.Exists(novoCaminho))
        {
            novoCaminho = Path.Combine(diretorio, $"{nomeSemExtensao}[NOVO] ({cont}){extensao}");
            cont++;
        }
        return novoCaminho;
    }

    private static void EscreverArquivoDoConteudo(ArquivoModel arquivo, string pastaDestinoEspecifica, ConfigModel config, byte[]? conteudoEspecifico = null, string? extensaoAlvoOverride = null)
    {
        byte[]? bytesParaEscrever = conteudoEspecifico ?? arquivo.Conteudo;
        if (bytesParaEscrever == null || bytesParaEscrever.Length == 0) return;

        string extensaoBase = extensaoAlvoOverride ?? arquivo.Extensao;
        string baseDestino = ObterPastaDestinoBase(extensaoBase, config);

        // Tratamento da pasta de categoria/OCR para não exceder o limite do Windows
        if (!string.IsNullOrWhiteSpace(pastaDestinoEspecifica))
        {
            // Limita a subpasta a 60 caracteres e remove caracteres inválidos
            pastaDestinoEspecifica = TratarNomeArquivoOuPasta(pastaDestinoEspecifica, 60);
            baseDestino = Path.Combine(baseDestino, pastaDestinoEspecifica);
        }

        if (!Directory.Exists(baseDestino))
            Directory.CreateDirectory(baseDestino);

        string nomeFinal = arquivo.Nome;
        if (extensaoAlvoOverride != null && !arquivo.Nome.ToLower().EndsWith(extensaoAlvoOverride.ToLower()))
        {
            nomeFinal = Path.GetFileNameWithoutExtension(arquivo.Nome) + extensaoAlvoOverride;
        }

        // Tratamento do nome do arquivo (limite de 120 caracteres para o corpo do nome)
        string ext = Path.GetExtension(nomeFinal);
        string nomeSemExt = Path.GetFileNameWithoutExtension(nomeFinal);
        nomeSemExt = TratarNomeArquivoOuPasta(nomeSemExt, 120);
        nomeFinal = nomeSemExt + ext;

        string caminhoFinal = Path.Combine(baseDestino, nomeFinal);
        caminhoFinal = ObterCaminhoComTagNovo(caminhoFinal);

        File.WriteAllBytes(caminhoFinal, bytesParaEscrever);

        if (File.Exists(arquivo.CaminhoCompleto))
        {
            try { File.Delete(arquivo.CaminhoCompleto); } catch { }
        }
    }

    private static string TratarNomeArquivoOuPasta(string nome, int tamanhoMaximo)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "sem_nome";

        // Remove caracteres inválidos para o sistema de arquivos
        var invalidChars = Path.GetInvalidFileNameChars();
        string textoLimpo = new string(nome.Where(c => !invalidChars.Contains(c)).ToArray());

        // Remove espaços duplos ou quebras de linha
        textoLimpo = Regex.Replace(textoLimpo, @"\s+", " ").Trim();

        // Trunca o nome se for maior que o permitido
        if (textoLimpo.Length > tamanhoMaximo)
            textoLimpo = textoLimpo.Substring(0, tamanhoMaximo).Trim();

        return string.IsNullOrWhiteSpace(textoLimpo) ? "arquivo_renomeado" : textoLimpo;
    }

    public static async Task ConverterImagensParaPdfComOcrAsync(string pastaRaiz, IEnumerable<ArquivoModel> arquivos, Func<bool> checkPaused, Action<string, bool, string>? reportarProgresso = null)
    {
        var config = LerConfiguracao();
        string[] extensoesImagem = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

        var arquivosImagem = arquivos.Where(f => extensoesImagem.Contains(f.Extensao.ToLower()) && f.Conteudo != null).ToList();

        if (arquivosImagem.Count == 0) return;

        try
        {
            OcrEngine engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("pt-BR"));
            if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();

            if (engine != null)
            {
                foreach (var arquivoImg in arquivosImagem)
                {
                    if (checkPaused != null && checkPaused())
                    {
                        reportarProgresso?.Invoke(arquivoImg.CaminhoCompleto, false, "PAUSADO - Caches salvos.");
                        while (checkPaused()) { await Task.Delay(500); }
                        reportarProgresso?.Invoke(arquivoImg.CaminhoCompleto, true, "Retomando...");
                    }

                    try
                    {
                        reportarProgresso?.Invoke(arquivoImg.CaminhoCompleto, true, "Convertendo OCR Windows...");

                        if (arquivoImg.Conteudo == null) continue;

                        using (var ras = new InMemoryRandomAccessStream())
                        {
                            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
                            {
                                writer.WriteBytes(arquivoImg.Conteudo);
                                await writer.StoreAsync();
                            }
                            var decoder = await BitmapDecoder.CreateAsync(ras);
                            var sb = await decoder.GetSoftwareBitmapAsync();
                            var ocrResult = await engine.RecognizeAsync(sb);

                            var builder = new PdfDocumentBuilder();
                            var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
                            var newPage = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);

                            double yPos = newPage.PageSize.Height - 20;
                            foreach (var line in ocrResult.Lines)
                            {
                                newPage.AddText(line.Text, 10, new PdfPoint(20, yPos), font);
                                yPos -= 12;
                            }

                            byte[] pdfGeradoBytes = builder.Build();
                            EscreverArquivoDoConteudo(arquivoImg, string.Empty, config, pdfGeradoBytes, ".pdf");
                            reportarProgresso?.Invoke(arquivoImg.CaminhoCompleto, false, "Convertido c/ OCR Nativo");
                        }
                    }
                    catch (Exception ex)
                    {
                        RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Falha OCR Imagem: {arquivoImg.Nome} - {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Erro fatal Windows OCR (Conversão): {ex.Message}");
        }
    }

    public static async Task OrganizarPdfsPorSimilaridadeOcrAsync(string pastaRaiz, IEnumerable<ArquivoModel> arquivos, Func<bool> checkPaused, Action<string, bool, string>? reportarProgresso = null)
    {
        var arquivosPdf = arquivos.Where(a => a.Extensao.ToLower() == ".pdf" && a.Conteudo != null).ToList();
        if (arquivosPdf.Count < 2) return;

        var config = LerConfiguracao();
        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        if (!Directory.Exists(pastaAppData)) Directory.CreateDirectory(pastaAppData);

        string caminhoCacheOcr = Path.Combine(pastaAppData, "ocr_cache.json");
        string caminhoCacheVisual = Path.Combine(pastaAppData, "visual_cache.json");

        var cacheOcr = new Dictionary<string, HashSet<string>>();
        var cacheVisual = new Dictionary<string, ulong>();

        if (File.Exists(caminhoCacheOcr))
        {
            try { cacheOcr = JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(File.ReadAllText(caminhoCacheOcr)) ?? new Dictionary<string, HashSet<string>>(); }
            catch { }
        }
        if (File.Exists(caminhoCacheVisual))
        {
            try { cacheVisual = JsonSerializer.Deserialize<Dictionary<string, ulong>>(File.ReadAllText(caminhoCacheVisual)) ?? new Dictionary<string, ulong>(); }
            catch { }
        }

        var documentosOcr = new List<(ArquivoModel Arquivo, byte[] ConteudoFinal, HashSet<string> Palavras)>();
        var documentosParaVisual = new List<(ArquivoModel Arquivo, byte[] ConteudoFinal)>();

        bool cacheOcrAlterado = false;
        bool cacheVisualAlterado = false;

        var algoritmoHash = new DifferenceHash();

        ulong ObterHashVisualDeMemoria(ArquivoModel arq, byte[]? conteudo)
        {
            if (conteudo == null) return 0;

            reportarProgresso?.Invoke(arq.CaminhoCompleto, true, "Calculando Hash Visual...");
            string chaveCache = $"{arq.Nome}_{conteudo.Length}";
            ulong hashResult = 0;

            if (cacheVisual.TryGetValue(chaveCache, out ulong hashSalvo))
            {
                hashResult = hashSalvo;
            }
            else
            {
                try
                {
                    using (var stream = new MemoryStream(conteudo))
                    using (var documentoPdf = UglyToad.PdfPig.PdfDocument.Open(stream))
                    {
                        var imagens = documentoPdf.GetPage(1).GetImages().ToList();
                        if (imagens.Count > 0)
                        {
                            var imagemPrincipal = imagens.OrderByDescending(img => img.RawBytes.Count).First();
                            using (var streamImg = new MemoryStream(imagemPrincipal.RawBytes.ToArray()))
                            using (var imgSharp = Image.Load<Rgba32>(streamImg))
                            {
                                hashResult = algoritmoHash.Hash(imgSharp);
                                cacheVisual[chaveCache] = hashResult;
                                cacheVisualAlterado = true;
                            }
                        }
                    }
                }
                catch { }
            }

            reportarProgresso?.Invoke(arq.CaminhoCompleto, false, hashResult != 0 ? "Hash Visual Concluído" : "Sem Imagem (Ignorado)");
            return hashResult;
        }

        foreach (var pdf in arquivosPdf)
        {
            if (pdf.Conteudo == null) continue;

            if (checkPaused != null && checkPaused())
            {
                if (cacheOcrAlterado)
                {
                    try { File.WriteAllText(caminhoCacheOcr, JsonSerializer.Serialize(cacheOcr)); cacheOcrAlterado = false; } catch { }
                }
                reportarProgresso?.Invoke(pdf.CaminhoCompleto, false, "PAUSADO - Caches salvos.");
                while (checkPaused()) { await Task.Delay(500); }
                reportarProgresso?.Invoke(pdf.CaminhoCompleto, true, "Retomando...");
            }

            reportarProgresso?.Invoke(pdf.CaminhoCompleto, true, "Lendo OCR Nativo (Texto)...");
            string chaveCache = $"{pdf.Nome}_{pdf.Conteudo.Length}";
            HashSet<string> palavras;
            byte[] conteudoFinal = pdf.Conteudo;

            if (cacheOcr.ContainsKey(chaveCache))
            {
                palavras = cacheOcr[chaveCache];
            }
            else
            {
                string textoBruto = await ExtrairTextoDoPdfEmMemoriaAsync(pdf, delegate (byte[] pdfModificadoBytes)
                {
                    if (pdfModificadoBytes != null)
                    {
                        conteudoFinal = pdfModificadoBytes;
                        pdf.Conteudo = conteudoFinal;
                        try { File.WriteAllBytes(pdf.CaminhoCompleto, conteudoFinal); } catch { }
                    }
                });

                string textoLimpo = LimparTextoParaOcr(textoBruto);
                palavras = new HashSet<string>(textoLimpo.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

                cacheOcr[chaveCache] = palavras;
                cacheOcrAlterado = true;
            }

            if (palavras.Count == 0)
            {
                documentosParaVisual.Add((pdf, conteudoFinal));
                reportarProgresso?.Invoke(pdf.CaminhoCompleto, false, "Enviado p/ Visual (Sem Texto)");
            }
            else if (palavras.Count >= 3)
            {
                documentosOcr.Add((pdf, conteudoFinal, palavras));
                reportarProgresso?.Invoke(pdf.CaminhoCompleto, false, "OCR Concluído");
            }
            else
            {
                reportarProgresso?.Invoke(pdf.CaminhoCompleto, false, "Ignorado (Pouco Texto)");
            }
        }

        if (cacheOcrAlterado)
        {
            try { File.WriteAllText(caminhoCacheOcr, JsonSerializer.Serialize(cacheOcr)); } catch { }
        }

        var clustersOcr = new List<List<(ArquivoModel Arquivo, byte[] ConteudoFinal, HashSet<string> Palavras)>>();
        double taxaCorteOcr = 0.15;

        foreach (var doc in documentosOcr)
        {
            bool adicionado = false;
            foreach (var cluster in clustersOcr)
            {
                double similaridade = CalcularSimilaridadeJaccard(doc.Palavras, cluster[0].Palavras);
                if (similaridade >= taxaCorteOcr)
                {
                    cluster.Add(doc);
                    adicionado = true;
                    break;
                }
            }
            if (!adicionado) clustersOcr.Add(new List<(ArquivoModel Arquivo, byte[] ConteudoFinal, HashSet<string> Palavras)> { doc });
        }

        foreach (var cluster in clustersOcr)
        {
            if (cluster.Count < 2) continue;

            var palavrasComuns = new HashSet<string>(cluster[0].Palavras);
            foreach (var doc in cluster.Skip(1)) palavrasComuns.IntersectWith(doc.Palavras);

            string nomeCategoria = string.Join("_", palavrasComuns.OrderByDescending(p => p.Length).Take(2));
            if (string.IsNullOrWhiteSpace(nomeCategoria)) nomeCategoria = "Documentos_Similares";
            else nomeCategoria = char.ToUpper(nomeCategoria[0]) + nomeCategoria.Substring(1);

            foreach (var doc in cluster)
            {
                EscreverArquivoDoConteudo(doc.Arquivo, nomeCategoria, config, doc.ConteudoFinal);
            }
        }

        if (documentosParaVisual.Count > 0)
        {
            var filaDeArquivosVisuais = new List<(ArquivoModel Arquivo, byte[] ConteudoFinal, ulong Hash)>();

            foreach (var doc in documentosParaVisual)
            {
                ulong hashResult = ObterHashVisualDeMemoria(doc.Arquivo, doc.ConteudoFinal);
                if (hashResult != 0) filaDeArquivosVisuais.Add((doc.Arquivo, doc.ConteudoFinal, hashResult));
            }

            double[] ondasDeTolerancia = { 85.0, 75.0, 65.0, 50.0 };

            string baseDestinoPDF = ObterPastaDestinoBase(".pdf", config);
            var templatesPastas = new Dictionary<string, ulong>();

            if (Directory.Exists(baseDestinoPDF))
            {
                var subpastas = Directory.GetDirectories(baseDestinoPDF);
                foreach (var subpasta in subpastas)
                {
                    var pdfExemplo = Directory.GetFiles(subpasta, "*.pdf", SearchOption.TopDirectoryOnly).FirstOrDefault();
                    if (pdfExemplo != null)
                    {
                        try
                        {
                            byte[] bytesBase = File.ReadAllBytes(pdfExemplo);
                            var mockModel = new ArquivoModel { Nome = Path.GetFileName(pdfExemplo), CaminhoCompleto = pdfExemplo };
                            ulong hashTemplate = ObterHashVisualDeMemoria(mockModel, bytesBase);
                            if (hashTemplate != 0) templatesPastas[Path.GetFileName(subpasta)] = hashTemplate;
                        }
                        catch { }
                    }
                }
            }

            foreach (double taxaCorte in ondasDeTolerancia)
            {
                if (filaDeArquivosVisuais.Count == 0) break;

                var arquivosQueSobraram = new List<(ArquivoModel Arquivo, byte[] ConteudoFinal, ulong Hash)>();

                foreach (var doc in filaDeArquivosVisuais)
                {
                    bool encontrouPasta = false;
                    foreach (var template in templatesPastas)
                    {
                        if (CompareHash.Similarity(doc.Hash, template.Value) >= taxaCorte)
                        {
                            EscreverArquivoDoConteudo(doc.Arquivo, template.Key, config, doc.ConteudoFinal);
                            encontrouPasta = true;
                            break;
                        }
                    }
                    if (!encontrouPasta) arquivosQueSobraram.Add(doc);
                }

                var clustersVisuais = new List<List<(ArquivoModel Arquivo, byte[] ConteudoFinal, ulong Hash)>>();
                foreach (var doc in arquivosQueSobraram)
                {
                    bool adicionado = false;
                    foreach (var cluster in clustersVisuais)
                    {
                        if (CompareHash.Similarity(doc.Hash, cluster[0].Hash) >= taxaCorte)
                        {
                            cluster.Add(doc);
                            adicionado = true;
                            break;
                        }
                    }
                    if (!adicionado) clustersVisuais.Add(new List<(ArquivoModel Arquivo, byte[] ConteudoFinal, ulong Hash)> { doc });
                }

                filaDeArquivosVisuais.Clear();

                foreach (var cluster in clustersVisuais)
                {
                    if (cluster.Count < 2)
                    {
                        filaDeArquivosVisuais.AddRange(cluster);
                        continue;
                    }

                    string tituloExtraido = await ObterTituloMaiorFonteMemoriaAsync(cluster[0].ConteudoFinal);
                    string sufixoTaxa = $"{taxaCorte}%";
                    string nomeBaseCategoria = string.IsNullOrWhiteSpace(tituloExtraido) ? sufixoTaxa : $"{tituloExtraido}_{sufixoTaxa}";

                    string nomeCategoria = nomeBaseCategoria;
                    int contador = 1;
                    string pastaDestino = Path.Combine(baseDestinoPDF, nomeCategoria);

                    while (Directory.Exists(pastaDestino))
                    {
                        nomeCategoria = $"{nomeBaseCategoria} ({contador})";
                        pastaDestino = Path.Combine(baseDestinoPDF, nomeCategoria);
                        contador++;
                    }

                    Directory.CreateDirectory(pastaDestino);
                    templatesPastas[nomeCategoria] = cluster[0].Hash;

                    foreach (var doc in cluster)
                    {
                        EscreverArquivoDoConteudo(doc.Arquivo, nomeCategoria, config, doc.ConteudoFinal);
                    }
                }
            }

            if (cacheVisualAlterado)
            {
                try { File.WriteAllText(caminhoCacheVisual, JsonSerializer.Serialize(cacheVisual)); } catch { }
            }
        }
    }

    private static async Task<string> ExtrairTextoDoPdfEmMemoriaAsync(ArquivoModel arq, Action<byte[]>? atualizouPdfCallback)
    {
        StringBuilder textoTotal = new StringBuilder();
        bool precisaOcr = false;

        if (arq.Conteudo == null) return string.Empty;

        try
        {
            using (var stream = new MemoryStream(arq.Conteudo))
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(stream))
            {
                foreach (var page in pdf.GetPages())
                {
                    string textoDaPagina = page.Text;
                    if (!string.IsNullOrWhiteSpace(textoDaPagina))
                    {
                        textoTotal.Append(textoDaPagina).Append(" ");
                    }
                }
            }
            if (textoTotal.Length < 50) precisaOcr = true;
        }
        catch { }

        if (precisaOcr)
        {
            textoTotal.Clear();
            try
            {
                OcrEngine engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("pt-BR"));
                if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();

                if (engine != null)
                {
                    using (var stream = new MemoryStream(arq.Conteudo))
                    using (var pdf = UglyToad.PdfPig.PdfDocument.Open(stream))
                    {
                        var builder = new PdfDocumentBuilder();
                        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
                        bool injetouAlgo = false;

                        foreach (var page in pdf.GetPages())
                        {
                            var pageBuilder = builder.AddPage(pdf, page.Number);
                            var imagens = page.GetImages();

                            foreach (var image in imagens)
                            {
                                byte[] imgBytes = image.RawBytes.ToArray();
                                using (var ras = new InMemoryRandomAccessStream())
                                {
                                    using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
                                    {
                                        writer.WriteBytes(imgBytes);
                                        await writer.StoreAsync();
                                    }
                                    var decoder = await BitmapDecoder.CreateAsync(ras);
                                    var sb = await decoder.GetSoftwareBitmapAsync();
                                    var ocrResult = await engine.RecognizeAsync(sb);

                                    textoTotal.Append(ocrResult.Text).Append(" ");

                                    if (!string.IsNullOrWhiteSpace(ocrResult.Text))
                                    {
                                        pageBuilder.AddText(ocrResult.Text, 1, new PdfPoint(5, 5), font);
                                        injetouAlgo = true;
                                    }
                                }
                            }
                        }

                        if (injetouAlgo)
                        {
                            byte[] pdfModificadoBytes = builder.Build();
                            atualizouPdfCallback?.Invoke(pdfModificadoBytes);
                        }
                    }
                }
            }
            catch { }
        }

        return textoTotal.ToString();
    }

    private static double CalcularSimilaridadeJaccard(HashSet<string> docA, HashSet<string> docB)
    {
        int intersecao = docA.Intersect(docB).Count();
        int uniao = docA.Union(docB).Count();

        if (uniao == 0) return 0;
        return (double)intersecao / uniao;
    }

    private static string LimparTextoParaOcr(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas",
            "por", "para", "com", "sem", "um", "uma", "uns", "umas", "os", "as", "ao", "aos",
            "que", "como", "sobre", "este", "este", "esse", "essa", "isso", "isto"
        };

        var textoNormalizado = texto.Normalize(NormalizationForm.FormD);
        var construtor = new StringBuilder();

        foreach (var c in textoNormalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                construtor.Append(c);
        }

        string semAcentos = construtor.ToString().Normalize(NormalizationForm.FormC).ToLower();
        string apenasLetras = Regex.Replace(semAcentos, @"[^a-z]", " ");

        var palavrasValidas = apenasLetras.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Where(p => p.Length > 3 && !stopWords.Contains(p));

        return string.Join(" ", palavrasValidas);
    }

    public static void Executar(string pastaRaiz, IEnumerable<ArquivoModel> arquivos)
    {
        var config = LerConfiguracao();

        if (Directory.Exists(pastaRaiz))
        {
            foreach (string sub in Directory.GetDirectories(pastaRaiz))
            {
                try
                {
                    var arqs = Directory.GetFiles(sub);
                    var dirs = Directory.GetDirectories(sub);
                    if (dirs.Length == 0 && arqs.Length > 0 && arqs.Length <= 2)
                    {
                        foreach (string arq in arqs)
                        {
                            FileInfo fi = new FileInfo(arq);
                            byte[]? dummyConteudo = null;
                            try { dummyConteudo = File.ReadAllBytes(arq); } catch { }
                            var arqMod = new ArquivoModel { Nome = fi.Name, Extensao = fi.Extension, CaminhoCompleto = arq, Conteudo = dummyConteudo };
                            EscreverArquivoDoConteudo(arqMod, string.Empty, config);
                        }
                        if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub, false);
                    }
                }
                catch { }
            }
        }

        if (arquivos == null || !arquivos.Any()) return;
        foreach (var arquivo in arquivos)
        {
            if (arquivo.Conteudo == null) continue;
            if (arquivo.Nome.Equals("extensoes_desconhecidas.txt", StringComparison.OrdinalIgnoreCase) ||
                arquivo.Nome.Equals("erros_organizacao.txt", StringComparison.OrdinalIgnoreCase))
            {
                arquivo.StatusSincronizacao = "Ignorado (Log)";
                continue;
            }

            try
            {
                EscreverArquivoDoConteudo(arquivo, string.Empty, config);
                arquivo.StatusSincronizacao = "Sincronizado p/ JSON";
            }
            catch (Exception ex)
            {
                arquivo.StatusSincronizacao = "Erro";
                RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Erro Executar RAM -> Destino: {ex.Message}");
            }
        }
    }

    public static void OrganizarPastasDesconhecidasPorConteudo(string pastaRaiz, IEnumerable<ArquivoModel> arquivos)
    {
        var pastas = arquivos.Where(a => a.Extensao == "Pasta de Arquivos").ToList();
        if (pastas.Count == 0) return;

        var configAtual = LerConfiguracao();
        string caminhoLogDesconhecidas = Path.Combine(pastaRaiz, "extensoes_desconhecidas.txt");
        string caminhoLogErros = Path.Combine(pastaRaiz, "erros_organizacao.txt");

        foreach (var pasta in pastas)
        {
            string caminhoPasta = pasta.CaminhoCompleto;
            string nomePasta = pasta.Nome;

            if (configAtual.Categorias.Keys.Any(c => c.Equals(nomePasta, StringComparison.OrdinalIgnoreCase))) continue;
            if (configAtual.Categorias.Any(c => c.Value.ContainsKey(nomePasta.ToLower()))) continue;

            try
            {
                var contagemExtensoes = new Dictionary<string, int>();
                var arquivosNaPasta = ObterArquivosSeguros(caminhoPasta);
                if (arquivosNaPasta.Count == 0) continue;

                foreach (string arq in arquivosNaPasta)
                {
                    string ext = Path.GetExtension(arq).Replace(".", "").ToLower();
                    if (string.IsNullOrWhiteSpace(ext)) ext = "sem_extensao";
                    if (contagemExtensoes.ContainsKey(ext)) contagemExtensoes[ext]++;
                    else contagemExtensoes[ext] = 1;
                }

                if (contagemExtensoes.Count > 0)
                {
                    string extensaoDominante = contagemExtensoes.OrderByDescending(x => x.Value).First().Key;
                    string? categoriaSugerida = configAtual.Categorias.FirstOrDefault(c => c.Value.ContainsKey(extensaoDominante)).Key;

                    if (!string.IsNullOrEmpty(categoriaSugerida))
                    {
                        foreach (string arq in arquivosNaPasta)
                        {
                            byte[]? conteudot = null;
                            try { conteudot = File.ReadAllBytes(arq); } catch { }
                            var mod = new ArquivoModel { Nome = Path.GetFileName(arq), Extensao = Path.GetExtension(arq), CaminhoCompleto = arq, Conteudo = conteudot };
                            EscreverArquivoDoConteudo(mod, nomePasta, configAtual);
                        }
                        try { if (!Directory.EnumerateFileSystemEntries(caminhoPasta).Any()) Directory.Delete(caminhoPasta, false); } catch { }
                    }
                    else
                    {
                        RegistrarLogGeral(caminhoLogDesconhecidas, $"A pasta '{nomePasta}' possui maioria de arquivos '.{extensaoDominante}', mas essa extensão não está cadastrada no JSON.");
                    }
                }
            }
            catch (Exception ex)
            {
                RegistrarLogGeral(caminhoLogErros, $"Falha ao processar a pasta '{nomePasta}': {ex.Message}");
            }
        }
    }

    public static void ReCategorizarPastasExistentes(string pastaRaiz, IEnumerable<ArquivoModel> arquivos)
    {
        var configAtual = LerConfiguracao();
        var pastas = arquivos.Where(a => a.Extensao == "Pasta de Arquivos").ToList();

        foreach (var pastaCategoria in pastas)
        {
            if (!configAtual.Categorias.Keys.Contains(pastaCategoria.Nome, StringComparer.OrdinalIgnoreCase)) continue;

            string[] subPastasExtensoes = Directory.GetDirectories(pastaCategoria.CaminhoCompleto);
            foreach (string caminhoSubPasta in subPastasExtensoes)
            {
                string nomeExtensao = Path.GetFileName(caminhoSubPasta).ToLower();
                string? categoriaCorreta = configAtual.Categorias.FirstOrDefault(c => c.Value.ContainsKey(nomeExtensao)).Key;

                if (!string.IsNullOrEmpty(categoriaCorreta) && !categoriaCorreta.Equals(pastaCategoria.Nome, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var arq in Directory.GetFiles(caminhoSubPasta))
                    {
                        byte[]? ct = null;
                        try { ct = File.ReadAllBytes(arq); } catch { }
                        var mod = new ArquivoModel { Nome = Path.GetFileName(arq), Extensao = Path.GetExtension(arq), CaminhoCompleto = arq, Conteudo = ct };
                        EscreverArquivoDoConteudo(mod, string.Empty, configAtual);
                    }
                    try { if (!Directory.EnumerateFileSystemEntries(caminhoSubPasta).Any()) Directory.Delete(caminhoSubPasta, false); } catch { }
                }
            }
        }
    }

    public static void OrganizarPastasPorPalavraChave(string pastaRaiz, IEnumerable<ArquivoModel> arquivos)
    {
        var config = LerConfiguracao();
        var arquivosReais = arquivos.Where(a => a.Conteudo != null).ToList();
        if (arquivosReais.Count == 0) return;

        var mapaPalavras = new Dictionary<string, List<ArquivoModel>>();
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas",
            "por", "para", "com", "sem", "um", "uma", "uns", "umas", "os", "as", "ao", "aos",
            "que", "como", "sobre"
        };

        foreach (var item in arquivosReais)
        {
            string nomeBase = Path.GetFileNameWithoutExtension(item.Nome);
            string nomeLimpo = LimparTexto(nomeBase);
            var palavras = nomeLimpo.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Distinct();

            foreach (var palavra in palavras)
            {
                if (palavra.Length <= 2 || stopWords.Contains(palavra)) continue;
                if (!mapaPalavras.ContainsKey(palavra)) mapaPalavras[palavra] = new List<ArquivoModel>();
                mapaPalavras[palavra].Add(item);
            }
        }

        var gruposOrdenados = mapaPalavras.Where(g => g.Value.Count >= 3).OrderByDescending(g => g.Value.Count).ToList();
        var itensMovidos = new HashSet<string>();

        foreach (var grupo in gruposOrdenados)
        {
            string palavraChave = grupo.Key;
            var itensDoGrupo = grupo.Value.Where(a => !itensMovidos.Contains(a.CaminhoCompleto)).ToList();

            if (itensDoGrupo.Count >= 3)
            {
                string nomePastaEspecifica = char.ToUpper(palavraChave[0]) + palavraChave.Substring(1);

                foreach (var item in itensDoGrupo)
                {
                    EscreverArquivoDoConteudo(item, nomePastaEspecifica, config);
                    itensMovidos.Add(item.CaminhoCompleto);
                }
            }
        }
    }

    public static void OrganizarPastasPorAnoMes(string pastaRaiz, IEnumerable<ArquivoModel> arquivos)
    {
        var config = LerConfiguracao();
        var arquivosReais = arquivos.Where(a => a.Conteudo != null).ToList();
        if (arquivosReais.Count == 0) return;

        var regexData = new Regex(@"_(20[1-9][0-9])([0-1][0-9])([0-3][0-9])?");

        foreach (var arquivo in arquivosReais)
        {
            if (arquivo.Nome.Equals("extensoes_desconhecidas.txt", StringComparison.OrdinalIgnoreCase) ||
                arquivo.Nome.Equals("erros_organizacao.txt", StringComparison.OrdinalIgnoreCase))
                continue;

            string pastaDestinoEspecifica = string.Empty;
            Match match = regexData.Match(arquivo.Nome);

            if (match.Success)
            {
                string ano = match.Groups[1].Value;
                string mes = match.Groups[2].Value;
                pastaDestinoEspecifica = $"{ano}_{mes}";
            }
            else
            {
                pastaDestinoEspecifica = arquivo.DataModificacao.ToString("yyyy_MM");
            }

            try
            {
                EscreverArquivoDoConteudo(arquivo, pastaDestinoEspecifica, config);
            }
            catch (Exception ex)
            {
                RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Erro ao mover '{arquivo.Nome}' por Ano/Mês: {ex.Message}");
            }
        }
    }

    private static string LimparTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        texto = Regex.Replace(texto, @"(?<=[a-zA-Z])(?=\d)|(?<=\d)(?=[a-zA-Z])", " ");

        var textoNormalizado = texto.Normalize(NormalizationForm.FormD);
        var construtor = new StringBuilder();

        foreach (var c in textoNormalizado)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                construtor.Append(c);
        }

        string semAcentos = construtor.ToString().Normalize(NormalizationForm.FormC).ToLower();
        return Regex.Replace(semAcentos, @"[^a-z0-9]", " ");
    }

    private static List<string> ObterArquivosSeguros(string raiz)
    {
        var arquivos = new List<string>();
        var pastasPendentes = new Queue<string>();
        pastasPendentes.Enqueue(raiz);

        while (pastasPendentes.Count > 0)
        {
            string pastaAtual = pastasPendentes.Dequeue();
            try
            {
                arquivos.AddRange(Directory.GetFiles(pastaAtual));
                foreach (var subDir in Directory.GetDirectories(pastaAtual)) pastasPendentes.Enqueue(subDir);
            }
            catch { }
        }
        return arquivos;
    }

    private static void RegistrarLogGeral(string caminhoArquivo, string mensagem)
    {
        try
        {
            string linha = $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss}] {mensagem}";
            File.AppendAllText(caminhoArquivo, linha + Environment.NewLine);
        }
        catch { }
    }

    public static void OrganizarPastasPorCategoria(string pastaRaiz, IEnumerable<ArquivoModel> arquivos)
    {
        var configAtual = LerConfiguracao();
        var pastas = arquivos.Where(a => a.Extensao == "Pasta de Arquivos").ToList();

        foreach (var pasta in pastas)
        {
            string nomePasta = pasta.Nome;
            if (configAtual.Categorias.Keys.Contains(nomePasta, StringComparer.OrdinalIgnoreCase)) continue;

            string? catDest = configAtual.Categorias.FirstOrDefault(c => c.Value.Keys.Contains(nomePasta.ToLower())).Key;
            if (!string.IsNullOrEmpty(catDest))
            {
                foreach (var arq in Directory.GetFiles(pasta.CaminhoCompleto))
                {
                    byte[]? ct = null;
                    try { ct = File.ReadAllBytes(arq); } catch { }
                    var mod = new ArquivoModel { Nome = Path.GetFileName(arq), Extensao = Path.GetExtension(arq), CaminhoCompleto = arq, Conteudo = ct };
                    EscreverArquivoDoConteudo(mod, string.Empty, configAtual);
                }
                try { if (!Directory.EnumerateFileSystemEntries(pasta.CaminhoCompleto).Any()) Directory.Delete(pasta.CaminhoCompleto, false); } catch { }
            }
        }
    }

    public static async Task OrganizarPdfsPorLayoutVisualAsync(string pastaRaiz, IEnumerable<ArquivoModel> arquivos, Func<bool> checkPaused, Action<string, bool, string>? reportarProgresso = null)
    {
        var arquivosPdf = arquivos.Where(a => a.Extensao.ToLower() == ".pdf" && a.Conteudo != null).ToList();
        if (arquivosPdf.Count == 0) return;

        var config = LerConfiguracao();
        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        if (!Directory.Exists(pastaAppData)) Directory.CreateDirectory(pastaAppData);
        string caminhoCache = Path.Combine(pastaAppData, "visual_cache.json");

        var cacheVisual = new Dictionary<string, ulong>();
        if (File.Exists(caminhoCache))
        {
            try { cacheVisual = JsonSerializer.Deserialize<Dictionary<string, ulong>>(File.ReadAllText(caminhoCache)) ?? new Dictionary<string, ulong>(); }
            catch { }
        }

        var algoritmoHash = new DifferenceHash();
        bool cacheAlterado = false;

        ulong ObterHashVisualDeMemoria(ArquivoModel arq)
        {
            if (arq.Conteudo == null) return 0;

            reportarProgresso?.Invoke(arq.CaminhoCompleto, true, "Calculando Hash Visual...");
            string chaveCache = $"{arq.Nome}_{arq.Conteudo.Length}";
            ulong hashResult = 0;

            if (cacheVisual.TryGetValue(chaveCache, out ulong hashSalvo))
            {
                hashResult = hashSalvo;
            }
            else
            {
                try
                {
                    using (var stream = new MemoryStream(arq.Conteudo))
                    using (var documentoPdf = UglyToad.PdfPig.PdfDocument.Open(stream))
                    {
                        var imagens = documentoPdf.GetPage(1).GetImages().ToList();
                        if (imagens.Count > 0)
                        {
                            var imagemPrincipal = imagens.OrderByDescending(img => img.RawBytes.Count).First();
                            using (var streamImg = new MemoryStream(imagemPrincipal.RawBytes.ToArray()))
                            using (var imgSharp = Image.Load<Rgba32>(streamImg))
                            {
                                hashResult = algoritmoHash.Hash(imgSharp);
                                cacheVisual[chaveCache] = hashResult;
                                cacheAlterado = true;
                            }
                        }
                    }
                }
                catch { }
            }

            reportarProgresso?.Invoke(arq.CaminhoCompleto, false, hashResult != 0 ? "Hash Concluído" : "Sem Imagem (Ignorado)");
            return hashResult;
        }

        var filaDeArquivos = new List<(ArquivoModel Arquivo, ulong Hash)>();
        foreach (var pdf in arquivosPdf)
        {
            if (checkPaused != null && checkPaused())
            {
                if (cacheAlterado)
                {
                    try { File.WriteAllText(caminhoCache, JsonSerializer.Serialize(cacheVisual)); cacheAlterado = false; } catch { }
                }
                reportarProgresso?.Invoke(pdf.CaminhoCompleto, false, "PAUSADO - Caches salvos.");
                while (checkPaused()) { await Task.Delay(500); }
                reportarProgresso?.Invoke(pdf.CaminhoCompleto, true, "Retomando...");
            }

            ulong hashAtual = ObterHashVisualDeMemoria(pdf);
            if (hashAtual != 0) filaDeArquivos.Add((pdf, hashAtual));
        }

        string baseDestinoPDF = ObterPastaDestinoBase(".pdf", config);
        var templatesPastas = new Dictionary<string, ulong>();

        if (Directory.Exists(baseDestinoPDF))
        {
            var subpastas = Directory.GetDirectories(baseDestinoPDF);
            foreach (var subpasta in subpastas)
            {
                var pdfExemplo = Directory.GetFiles(subpasta, "*.pdf", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (pdfExemplo != null)
                {
                    try
                    {
                        byte[] bytesBase = File.ReadAllBytes(pdfExemplo);
                        var mockModel = new ArquivoModel { Nome = Path.GetFileName(pdfExemplo), CaminhoCompleto = pdfExemplo, Conteudo = bytesBase };
                        ulong hashTemplate = ObterHashVisualDeMemoria(mockModel);
                        if (hashTemplate != 0) templatesPastas[Path.GetFileName(subpasta)] = hashTemplate;
                    }
                    catch { }
                }
            }
        }

        double[] ondasDeTolerancia = { 85.0, 75.0, 65.0, 50.0 };

        foreach (double taxaCorte in ondasDeTolerancia)
        {
            if (filaDeArquivos.Count == 0) break;

            var arquivosQueSobraram = new List<(ArquivoModel Arquivo, ulong Hash)>();

            foreach (var doc in filaDeArquivos)
            {
                bool encontrouPastaExistente = false;

                foreach (var template in templatesPastas)
                {
                    if (CompareHash.Similarity(doc.Hash, template.Value) >= taxaCorte)
                    {
                        EscreverArquivoDoConteudo(doc.Arquivo, template.Key, config);
                        encontrouPastaExistente = true;
                        break;
                    }
                }

                if (!encontrouPastaExistente)
                {
                    arquivosQueSobraram.Add(doc);
                }
            }

            var clusters = new List<List<(ArquivoModel Arquivo, ulong Hash)>>();

            foreach (var doc in arquivosQueSobraram)
            {
                bool adicionado = false;
                foreach (var cluster in clusters)
                {
                    if (CompareHash.Similarity(doc.Hash, cluster[0].Hash) >= taxaCorte)
                    {
                        cluster.Add(doc);
                        adicionado = true;
                        break;
                    }
                }
                if (!adicionado) clusters.Add(new List<(ArquivoModel Arquivo, ulong Hash)> { doc });
            }

            filaDeArquivos.Clear();

            foreach (var cluster in clusters)
            {
                if (cluster.Count < 2)
                {
                    filaDeArquivos.AddRange(cluster);
                    continue;
                }

                string tituloExtraido = await ObterTituloMaiorFonteMemoriaAsync(cluster[0].Arquivo.Conteudo);
                string sufixoTaxa = $"{taxaCorte}%";

                string nomeBaseCategoria = string.IsNullOrWhiteSpace(tituloExtraido)
                                            ? sufixoTaxa
                                            : $"{tituloExtraido}_{sufixoTaxa}";

                string nomeCategoria = nomeBaseCategoria;
                int contador = 1;
                string pastaDestino = Path.Combine(baseDestinoPDF, nomeCategoria);

                while (Directory.Exists(pastaDestino))
                {
                    nomeCategoria = $"{nomeBaseCategoria} ({contador})";
                    pastaDestino = Path.Combine(baseDestinoPDF, nomeCategoria);
                    contador++;
                }

                Directory.CreateDirectory(pastaDestino);
                templatesPastas[nomeCategoria] = cluster[0].Hash;

                foreach (var doc in cluster)
                {
                    EscreverArquivoDoConteudo(doc.Arquivo, nomeCategoria, config);
                }
            }
        }

        if (cacheAlterado)
        {
            try { File.WriteAllText(caminhoCache, JsonSerializer.Serialize(cacheVisual)); } catch { }
        }
    }

    private static async Task<string> ObterTituloMaiorFonteMemoriaAsync(byte[]? conteudoPdf)
    {
        if (conteudoPdf == null || conteudoPdf.Length == 0) return string.Empty;

        string tituloEncontrado = string.Empty;
        int maiorAlturaEncontrada = 0;

        try
        {
            using (var stream = new MemoryStream(conteudoPdf))
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(stream))
            {
                var page = pdf.GetPage(1);
                var letras = page.Letters.ToList();

                if (letras.Count > 0)
                {
                    var maiorLetra = letras.OrderByDescending(l => l.PointSize).First();
                    var letrasDoTitulo = letras.Where(l =>
                        Math.Abs(l.GlyphRectangle.Bottom - maiorLetra.GlyphRectangle.Bottom) < maiorLetra.PointSize &&
                        Math.Abs(l.PointSize - maiorLetra.PointSize) < 2)
                        .OrderBy(l => l.GlyphRectangle.Left);

                    tituloEncontrado = string.Join("", letrasDoTitulo.Select(l => l.Value)).Trim();

                    if (!string.IsNullOrWhiteSpace(tituloEncontrado) && tituloEncontrado.Length > 2)
                        return LimparNomePasta(tituloEncontrado);
                }

                var imagens = page.GetImages().ToList();
                if (imagens.Count > 0)
                {
                    var imagemBase = imagens.OrderByDescending(i => i.RawBytes.Count).First();
                    byte[] imgBytes = imagemBase.RawBytes.ToArray();

                    OcrEngine engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("pt-BR"));
                    if (engine == null) engine = OcrEngine.TryCreateFromUserProfileLanguages();

                    if (engine != null)
                    {
                        using (var ras = new InMemoryRandomAccessStream())
                        {
                            using (var writer = new DataWriter(ras.GetOutputStreamAt(0)))
                            {
                                writer.WriteBytes(imgBytes);
                                await writer.StoreAsync();
                            }
                            var decoder = await BitmapDecoder.CreateAsync(ras);
                            var sb = await decoder.GetSoftwareBitmapAsync();
                            var ocrResult = await engine.RecognizeAsync(sb);

                            foreach (var line in ocrResult.Lines)
                            {
                                if (line.Words.Count > 0)
                                {
                                    double maxLineHeight = line.Words.Max(w => w.BoundingRect.Height);
                                    if (maxLineHeight > maiorAlturaEncontrada)
                                    {
                                        string textoLinha = line.Text;
                                        if (!string.IsNullOrWhiteSpace(textoLinha) && textoLinha.Trim().Length > 3)
                                        {
                                            maiorAlturaEncontrada = (int)maxLineHeight;
                                            tituloEncontrado = textoLinha;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return LimparNomePasta(tituloEncontrado);
    }

    private static string LimparNomePasta(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        string textoLimpo = new string(texto.Where(c => !invalidChars.Contains(c)).ToArray());

        textoLimpo = Regex.Replace(textoLimpo, @"\s+", " ").Trim();

        if (textoLimpo.Length > 40) textoLimpo = textoLimpo.Substring(0, 40).Trim();

        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(textoLimpo.ToLower());
    }
}