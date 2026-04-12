using FileSyncX.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using Tesseract;
using CoenM.ImageHash;
using CoenM.ImageHash.HashAlgorithms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FileSyncX.Funcoes.GerenciadorArquivos;

public static class MoverEOrganizarArquivos
{
    private static readonly string[] ExtensoesShapefile = new[]
    {
        ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx",
        ".atx", ".fbn", ".fbx", ".qmd", ".ain", ".aih", ".ixs", ".mxs"
    };

    public static void ConverterImagensParaPdfComOcr(string pastaRaiz)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz)) return;

        // Extensões de imagem suportadas
        string[] extensoesImagem = { ".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff" };

        var arquivosImagem = Directory.GetFiles(pastaRaiz, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => extensoesImagem.Contains(Path.GetExtension(f).ToLower()))
            .ToList();

        if (arquivosImagem.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("[Conversão] Nenhuma imagem encontrada para converter.");
            return;
        }

        string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        if (!Directory.Exists(tessDataPath))
        {
            System.Diagnostics.Debug.WriteLine("[Conversão-ERRO] Pasta 'tessdata' não encontrada. Abortando.");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[Conversão] {arquivosImagem.Count} imagens encontradas. Iniciando conversão para PDF (OCR)...");

        // Inicializa o Tesseract Engine
        try
        {
            using (var engine = new TesseractEngine(tessDataPath, "por", EngineMode.Default))
            {
                foreach (var caminhoImagem in arquivosImagem)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[Conversão] Processando: {Path.GetFileName(caminhoImagem)}");

                        string diretorio = Path.GetDirectoryName(caminhoImagem);
                        string nomeSemExtensao = Path.GetFileNameWithoutExtension(caminhoImagem);
                        
                        // O Tesseract PdfResultRenderer adiciona a extensão .pdf automaticamente, 
                        // então fornecemos o caminho sem a extensão
                        string caminhoPdfSaida = Path.Combine(diretorio, nomeSemExtensao);

                        // Cria o renderizador de PDF. O segundo parâmetro é o caminho da pasta tessdata (necessário para a fonte pdf.ttf interna do Tesseract)
                        // O parâmetro 'false' no final indica que não é textonly, ou seja, vai manter a imagem no fundo.
                        // Cria o renderizador de PDF. O segundo parâmetro é o caminho da pasta tessdata
                        // O parâmetro 'false' no final indica que não é textonly, ou seja, vai manter a imagem no fundo.
                        using (IResultRenderer renderer = Tesseract.PdfResultRenderer.CreatePdfRenderer(caminhoPdfSaida, tessDataPath, false))
                        {
                            // CORREÇÃO AQUI: BeginDocument agora é um bloco 'using' e o EndDocument foi removido.
                            using (renderer.BeginDocument(nomeSemExtensao))
                            {
                                using (var img = Pix.LoadFromFile(caminhoImagem))
                                {
                                    using (var page = engine.Process(img))
                                    {
                                        // Adiciona a imagem e a camada de texto OCR no PDF
                                        renderer.AddPage(page);
                                    }
                                }
                            } // Ao fechar esta chave, o Tesseract automaticamente "finaliza" (EndDocument) e salva o PDF.
                        }

                        // Verifica se o PDF foi criado com sucesso (o Tesseract anexa ".pdf" ao nome fornecido)
                        string caminhoPdfGerado = caminhoPdfSaida + ".pdf";
                        
                        if (File.Exists(caminhoPdfGerado))
                        {
                            // PDF gerado com sucesso, agora podemos deletar a imagem original
                            File.Delete(caminhoImagem);
                            System.Diagnostics.Debug.WriteLine($"[Conversão] Sucesso! Convertido para: {Path.GetFileName(caminhoPdfGerado)}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[Conversão-ERRO] Falha ao salvar o PDF para: {nomeSemExtensao}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Conversão-ERRO] Erro ao processar a imagem {Path.GetFileName(caminhoImagem)}: {ex.Message}");
                        RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Falha OCR Imagem: {Path.GetFileName(caminhoImagem)} - {ex.Message}");
                    }
                }
            }
            
            System.Diagnostics.Debug.WriteLine("[Conversão] Processo concluído!");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Conversão-ERRO] Falha ao iniciar a Engine do Tesseract: {ex.Message}");
            RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Erro fatal Tesseract (Conversão): {ex.Message}");
        }
    }



    public static void OrganizarPdfsPorSimilaridadeOcr(string pastaRaiz, Action<string, bool, string> reportarProgresso = null)
    {
        var arquivosPdf = Directory.GetFiles(pastaRaiz, "*.pdf", SearchOption.TopDirectoryOnly);
        System.Diagnostics.Debug.WriteLine($"[OCR] Encontrados {arquivosPdf.Length} arquivos PDF na pasta.");
        
        if (arquivosPdf.Length < 2) return;

        // Configuração de Caches Híbridos (Texto e Imagem)
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

        var documentosOcr = new List<(string Caminho, HashSet<string> Palavras)>();
        var documentosParaVisual = new List<string>(); 
        
        bool cacheOcrAlterado = false;
        bool cacheVisualAlterado = false;

        var algoritmoHash = new DifferenceHash();

        // -------------------------------------------------------------
        // FUNÇÃO LOCAL: Obter Hash Visual com Cache Integrado e Status
        // -------------------------------------------------------------
        ulong ObterHashVisual(string caminhoPdf)
        {
            reportarProgresso?.Invoke(caminhoPdf, true, "Calculando Hash Visual...");

            var info = new FileInfo(caminhoPdf);
            string chaveCache = $"{info.Name}_{info.Length}";
            ulong hashResult = 0;

            if (cacheVisual.TryGetValue(chaveCache, out ulong hashSalvo))
            {
                hashResult = hashSalvo;
            }
            else
            {
                try
                {
                    using (var documentoPdf = UglyToad.PdfPig.PdfDocument.Open(caminhoPdf))
                    {
                        var imagens = documentoPdf.GetPage(1).GetImages().ToList();
                        if (imagens.Count > 0)
                        {
                            var imagemPrincipal = imagens.OrderByDescending(img => img.RawBytes.Count).First();
                            using (var imgSharp = Image.Load<Rgba32>(imagemPrincipal.RawBytes.ToArray()))
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

            reportarProgresso?.Invoke(caminhoPdf, false, hashResult != 0 ? "Hash Visual Concluído" : "Sem Imagem (Ignorado)");
            return hashResult;
        }

        // =======================================================
        // ETAPA 1: Leitura Primária (Motor de Texto OCR + Injeção)
        // =======================================================
        foreach (var pdf in arquivosPdf)
        {
            reportarProgresso?.Invoke(pdf, true, "Lendo OCR (Texto)...");

            var infoArquivo = new FileInfo(pdf);
            string chaveCache = $"{infoArquivo.Name}_{infoArquivo.Length}";

            HashSet<string> palavras;

            if (cacheOcr.ContainsKey(chaveCache))
            {
                palavras = cacheOcr[chaveCache];
            }
            else
            {
                // Extrai o texto e injeta a camada OCR se o PDF for uma imagem pura
                string textoBruto = ExtrairTextoDoPdf(pdf, out bool pdfModificado);
                
                // Se foi injetado OCR, o arquivo físico foi alterado. Atualizamos a chave de cache!
                if (pdfModificado)
                {
                    infoArquivo = new FileInfo(pdf);
                    chaveCache = $"{infoArquivo.Name}_{infoArquivo.Length}";
                }

                string textoLimpo = LimparTextoParaOcr(textoBruto);
                palavras = new HashSet<string>(textoLimpo.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
                
                cacheOcr[chaveCache] = palavras;
                cacheOcrAlterado = true;
            }

            if (palavras.Count == 0)
            {
                documentosParaVisual.Add(pdf);
                reportarProgresso?.Invoke(pdf, false, "Enviado p/ Visual (Sem Texto)");
            }
            else if (palavras.Count >= 3) 
            {
                documentosOcr.Add((pdf, palavras));
                reportarProgresso?.Invoke(pdf, false, "OCR Concluído");
            }
            else
            {
                reportarProgresso?.Invoke(pdf, false, "Ignorado (Pouco Texto)");
            }
        }

        if (cacheOcrAlterado)
        {
            try { File.WriteAllText(caminhoCacheOcr, JsonSerializer.Serialize(cacheOcr)); } catch { }
        }

        // =======================================================
        // ETAPA 2: Clusterização dos Arquivos com Texto
        // =======================================================
        var clustersOcr = new List<List<(string Caminho, HashSet<string> Palavras)>>();
        double taxaCorteOcr = 0.18; // 18% 

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
            if (!adicionado) clustersOcr.Add(new List<(string Caminho, HashSet<string> Palavras)> { doc });
        }

        foreach (var cluster in clustersOcr)
        {
            if (cluster.Count < 2) continue;

            var palavrasComuns = new HashSet<string>(cluster[0].Palavras);
            foreach (var doc in cluster.Skip(1)) palavrasComuns.IntersectWith(doc.Palavras);

            string nomeCategoria = string.Join("_", palavrasComuns.OrderByDescending(p => p.Length).Take(2));
            if (string.IsNullOrWhiteSpace(nomeCategoria)) nomeCategoria = "Documentos_Similares";
            else nomeCategoria = char.ToUpper(nomeCategoria[0]) + nomeCategoria.Substring(1);

            string pastaDestino = Path.Combine(pastaRaiz, nomeCategoria);
            if (!Directory.Exists(pastaDestino)) Directory.CreateDirectory(pastaDestino);

            foreach (var doc in cluster)
            {
                try
                {
                    string destinoFinal = Path.Combine(pastaDestino, Path.GetFileName(doc.Caminho));
                    if (!File.Exists(destinoFinal)) File.Move(doc.Caminho, destinoFinal);
                }
                catch { }
            }
        }

        // =======================================================
        // ETAPA 3: Fallback Automático (Motor de Imagem / Layout)
        // =======================================================
        if (documentosParaVisual.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[OCR-HÍBRIDO] Iniciando fallback visual para {documentosParaVisual.Count} PDFs sem texto.");
            
            var filaDeArquivosVisuais = new List<(string Caminho, ulong Hash)>();

            foreach (var pdf in documentosParaVisual)
            {
                if (!File.Exists(pdf)) continue;

                ulong hashResult = ObterHashVisual(pdf);
                if (hashResult != 0) filaDeArquivosVisuais.Add((pdf, hashResult));
            }

            double[] ondasDeTolerancia = { 85.0, 75.0, 65.0, 50.0 }; // Desce até 50%
            
            var templatesPastas = new Dictionary<string, ulong>();
            var subpastas = Directory.GetDirectories(pastaRaiz);
            
            // Usa o método local para garantir que os moldes das pastas sejam cacheados
            foreach (var subpasta in subpastas)
            {
                var pdfExemplo = Directory.GetFiles(subpasta, "*.pdf", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (pdfExemplo != null)
                {
                    ulong hashTemplate = ObterHashVisual(pdfExemplo);
                    if (hashTemplate != 0) templatesPastas[subpasta] = hashTemplate;
                }
            }

            foreach (double taxaCorte in ondasDeTolerancia)
            {
                if (filaDeArquivosVisuais.Count == 0) break;

                var arquivosQueSobraram = new List<(string Caminho, ulong Hash)>();

                // A: Tentar encaixar nas pastas existentes
                foreach (var doc in filaDeArquivosVisuais)
                {
                    bool encontrouPasta = false;
                    foreach (var template in templatesPastas)
                    {
                        if (CompareHash.Similarity(doc.Hash, template.Value) >= taxaCorte)
                        {
                            try
                            {
                                string destino = Path.Combine(template.Key, Path.GetFileName(doc.Caminho));
                                if (!File.Exists(destino)) File.Move(doc.Caminho, destino);
                                encontrouPasta = true;
                                break;
                            }
                            catch { }
                        }
                    }
                    if (!encontrouPasta) arquivosQueSobraram.Add(doc);
                }

                // B: Formar novos clusters
                var clustersVisuais = new List<List<(string Caminho, ulong Hash)>>();
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
                    if (!adicionado) clustersVisuais.Add(new List<(string Caminho, ulong Hash)> { doc });
                }

                filaDeArquivosVisuais.Clear();

                // C: Criar Pastas com Títulos Inteligentes
                foreach (var cluster in clustersVisuais)
                {
                    if (cluster.Count < 2)
                    {
                        filaDeArquivosVisuais.AddRange(cluster);
                        continue;
                    }

                    string tituloExtraido = ObterTituloMaiorFonte(cluster[0].Caminho);
                    string sufixoTaxa = $"{taxaCorte}%";
                    string nomeBaseCategoria = string.IsNullOrWhiteSpace(tituloExtraido) ? sufixoTaxa : $"{tituloExtraido}_{sufixoTaxa}";
                    
                    string nomeCategoria = nomeBaseCategoria;
                    int contador = 1;
                    string pastaDestino = Path.Combine(pastaRaiz, nomeCategoria);
                    
                    while (Directory.Exists(pastaDestino))
                    {
                        nomeCategoria = $"{nomeBaseCategoria} ({contador})";
                        pastaDestino = Path.Combine(pastaRaiz, nomeCategoria);
                        contador++;
                    }

                    Directory.CreateDirectory(pastaDestino);
                    templatesPastas[pastaDestino] = cluster[0].Hash;

                    foreach (var doc in cluster)
                    {
                        try
                        {
                            string destinoFinal = Path.Combine(pastaDestino, Path.GetFileName(doc.Caminho));
                            if (!File.Exists(destinoFinal)) File.Move(doc.Caminho, destinoFinal);
                        }
                        catch { }
                    }
                }
            }

            if (cacheVisualAlterado)
            {
                try { File.WriteAllText(caminhoCacheVisual, JsonSerializer.Serialize(cacheVisual)); } catch { }
            }
        }
    }

private static string ExtrairTextoDoPdf(string caminhoPdf, out bool pdfModificado)
    {
        StringBuilder textoTotal = new StringBuilder();
        pdfModificado = false;
        bool precisaOcr = false;

        try
        {
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(caminhoPdf))
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

            // Se tem menos de 50 caracteres, ativamos a "Injeção OCR"
            if (textoTotal.Length < 50) 
                precisaOcr = true;
        }
        catch { }

        if (precisaOcr)
        {
            textoTotal.Clear();
            string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            
            if (Directory.Exists(tessDataPath))
            {
                try
                {
                    string diretorio = Path.GetDirectoryName(caminhoPdf);
                    string nomeSemExtensao = Path.GetFileNameWithoutExtension(caminhoPdf);
                    
                    // Usamos um arquivo temporário para não corromper o original durante a leitura
                    string caminhoTemp = Path.Combine(diretorio, nomeSemExtensao + "_tempOcr");

                    using (var engine = new TesseractEngine(tessDataPath, "por", EngineMode.Default))
                    using (IResultRenderer renderer = Tesseract.PdfResultRenderer.CreatePdfRenderer(caminhoTemp, tessDataPath, false))
                    {
                        using (renderer.BeginDocument(nomeSemExtensao))
                        {
                            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(caminhoPdf))
                            {
                                foreach (var page in pdf.GetPages())
                                {
                                    var imagens = page.GetImages();
                                    foreach (var image in imagens)
                                    {
                                        byte[] imgBytes = image.RawBytes.ToArray();
                                        using (var pix = Pix.LoadFromMemory(imgBytes))
                                        using (var tesseractPage = engine.Process(pix))
                                        {
                                            // Extrai o texto e renderiza a camada invisível para o novo PDF
                                            textoTotal.Append(tesseractPage.GetText()).Append(" ");
                                            renderer.AddPage(tesseractPage);
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Se a renderização terminou bem, sobrescrevemos o PDF original
                    string pdfGerado = caminhoTemp + ".pdf";
                    if (File.Exists(pdfGerado))
                    {
                        File.Move(pdfGerado, caminhoPdf, true);
                        pdfModificado = true; // Sinaliza para atualizar o Cache JSON
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OCR-INJECTION] Erro ao injetar OCR no PDF {Path.GetFileName(caminhoPdf)}: {ex.Message}");
                }
            }
        }

        return textoTotal.ToString();
    }

    private static string ExtrairTextoDoPdf(string caminhoPdf)
    {
        StringBuilder textoTotal = new StringBuilder();

        try
        {
            // O PdfPig é excelente, vamos usá-lo para extrair texto nativo e também imagens
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(caminhoPdf))
            {
                foreach (var page in pdf.GetPages())
                {
                    // 1. Tenta extrair texto nativo digital
                    string textoDaPagina = page.Text;
                    if (!string.IsNullOrWhiteSpace(textoDaPagina))
                    {
                        textoTotal.Append(textoDaPagina).Append(" ");
                    }
                    
                    // 2. Se a página não tiver texto nativo (é um scan), pede para o PdfPig extrair as imagens da página
                    if (string.IsNullOrWhiteSpace(textoDaPagina) || textoDaPagina.Length < 20)
                    {
                        string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                        
                        if (Directory.Exists(tessDataPath))
                        {
                            var imagens = page.GetImages();
                            using (var engine = new TesseractEngine(tessDataPath, "por", EngineMode.Default))
                            {
                                foreach (var image in imagens)
                                {
                                    // Tesseract precisa de Bytes. O PdfPig fornece os Bytes da imagem raw.
                                    byte[] imgBytes = image.RawBytes.ToArray();
                                    
                                    try
                                    {
                                        using (var pix = Pix.LoadFromMemory(imgBytes))
                                        using (var tesseractPage = engine.Process(pix))
                                        {
                                            textoTotal.Append(tesseractPage.GetText()).Append(" ");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                         System.Diagnostics.Debug.WriteLine($"[OCR-ERROR] Falha no Tesseract na imagem do PDF: {ex.Message}");
                                    }
                                }
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("[OCR-ERROR] Pasta 'tessdata' não encontrada. Tesseract não rodou.");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"[OCR-ERROR] Erro fatal lendo o PDF {Path.GetFileName(caminhoPdf)}: {ex.Message}");
        }

        return textoTotal.ToString();
    }        private static double CalcularSimilaridadeJaccard(HashSet<string> docA, HashSet<string> docB)
        {
            // Fórmula de Jaccard: (Interseção) / (União)
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
        if (string.IsNullOrWhiteSpace(pastaRaiz) || arquivos == null) return;

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
                            MoverArquivoParaDestino(pastaRaiz, fi.FullName, fi.Name, fi.Extension, out _);
                        }
                        if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub, false);
                    }
                }
                catch { }
            }
        }

        if (!arquivos.Any()) return;
        foreach (var arquivo in arquivos)
        {
            if (arquivo.Nome.Equals("extensoes_desconhecidas.txt", StringComparison.OrdinalIgnoreCase) ||
                arquivo.Nome.Equals("erros_organizacao.txt", StringComparison.OrdinalIgnoreCase))
            {
                arquivo.StatusSincronizacao = "Ignorado (Log)";
                continue;
            }

            string status = MoverArquivoParaDestino(pastaRaiz, arquivo.CaminhoCompleto, arquivo.Nome, arquivo.Extensao, out string novoC);
            arquivo.StatusSincronizacao = status;
            if (status.StartsWith("Sincronizado") || status.StartsWith("Atualizado") || status.StartsWith("Já Existe"))
            {
                arquivo.CaminhoCompleto = novoC;
            }
        }
    }

    private static string MoverArquivoParaDestino(string pastaRaiz, string caminhoOrigem, string nomeArquivo, string extensao, out string caminhoFinal)
    {
        caminhoFinal = caminhoOrigem;
        if (!File.Exists(caminhoOrigem)) return "Não Encontrado";

        try
        {
            string pDest;
            if (EhArquivoShapefile(nomeArquivo, out string nomeBase)) pDest = Path.Combine(pastaRaiz, "shp", nomeBase);
            else
            {
                string extL = extensao.Replace(".", "").ToLower();
                pDest = Path.Combine(pastaRaiz, string.IsNullOrWhiteSpace(extL) ? "sem_extensao" : extL);
            }

            if (!Directory.Exists(pDest)) Directory.CreateDirectory(pDest);

            string cDest = Path.Combine(pDest, nomeArquivo);

            if (!File.Exists(cDest))
            {
                File.Move(caminhoOrigem, cDest);
                caminhoFinal = cDest;
                return "Sincronizado";
            }
            else
            {
                DateTime dataOrigem = File.GetLastWriteTime(caminhoOrigem);
                DateTime dataDestino = File.GetLastWriteTime(cDest);

                if (dataOrigem > dataDestino)
                {
                    File.Move(caminhoOrigem, cDest, true);
                    caminhoFinal = cDest;
                    return "Atualizado (Mais Recente)";
                }
                else
                {
                    File.Delete(caminhoOrigem);
                    caminhoFinal = cDest;
                    return "Já Existe (Destino Mantido)";
                }
            }
        }
        catch (Exception ex)
        {
            RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Erro ao mover o arquivo '{nomeArquivo}': {ex.Message}");
            return "Erro";
        }
    }

    public static void OrganizarPastasDesconhecidasPorConteudo(string pastaRaiz)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz)) return;

        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        string caminhoArquivoJson = Path.Combine(pastaAppData, "extensoes_config.json");
        if (!File.Exists(caminhoArquivoJson)) return;

        ConfigModel configAtual;
        try { configAtual = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(caminhoArquivoJson)) ?? new ConfigModel(); }
        catch { return; }

        string[] diretoriosNaRaiz = Directory.GetDirectories(pastaRaiz);
        var nomesCategorias = configAtual.Categorias.Keys.ToList();

        string caminhoLogDesconhecidas = Path.Combine(pastaRaiz, "extensoes_desconhecidas.txt");
        string caminhoLogErros = Path.Combine(pastaRaiz, "erros_organizacao.txt");

        foreach (string caminhoPasta in diretoriosNaRaiz)
        {
            string nomePasta = Path.GetFileName(caminhoPasta);

            if (nomesCategorias.Any(c => c.Equals(nomePasta, StringComparison.OrdinalIgnoreCase))) continue;
            if (configAtual.Categorias.Any(c => c.Value.ContainsKey(nomePasta.ToLower()))) continue;

            try
            {
                var resultadoAnalise = ObterExtensaoECategoriaDominante(caminhoPasta, configAtual);
                string extensaoDominante = resultadoAnalise.Extensao;
                string categoriaSugerida = resultadoAnalise.Categoria;

                if (!string.IsNullOrEmpty(categoriaSugerida))
                {
                    string pastaAlvo = Path.Combine(pastaRaiz, categoriaSugerida);
                    if (!Directory.Exists(pastaAlvo)) Directory.CreateDirectory(pastaAlvo);

                    string destinoFinal = Path.Combine(pastaAlvo, nomePasta);

                    if (!Directory.Exists(destinoFinal))
                    {
                        if (!Directory.EnumerateFileSystemEntries(caminhoPasta).Any())
                            Directory.Delete(caminhoPasta, false);
                        else
                            Directory.Move(caminhoPasta, destinoFinal);
                    }
                    else
                    {
                        MoverConteudoInterno(caminhoPasta, destinoFinal);
                        if (!Directory.EnumerateFileSystemEntries(caminhoPasta).Any()) Directory.Delete(caminhoPasta, false);
                    }
                }
                else if (!string.IsNullOrEmpty(extensaoDominante))
                {
                    RegistrarLogGeral(caminhoLogDesconhecidas, $"A pasta '{nomePasta}' possui maioria de arquivos '.{extensaoDominante}', mas essa extensão não está cadastrada no JSON.");
                }
            }
            catch (Exception ex)
            {
                RegistrarLogGeral(caminhoLogErros, $"Falha ao processar a pasta '{nomePasta}': {ex.Message}");
            }
        }
    }

    public static void ReCategorizarPastasExistentes(string pastaRaiz)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz)) return;

        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        string caminhoArquivoJson = Path.Combine(pastaAppData, "extensoes_config.json");
        if (!File.Exists(caminhoArquivoJson)) return;

        ConfigModel configAtual;
        try { configAtual = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(caminhoArquivoJson)) ?? new ConfigModel(); }
        catch { return; }

        var categoriasNoJson = configAtual.Categorias.Keys.ToList();
        string[] pastasCategoriasFisicas = Directory.GetDirectories(pastaRaiz);

        foreach (string caminhoCategoriaAtual in pastasCategoriasFisicas)
        {
            string nomeCategoriaAtual = Path.GetFileName(caminhoCategoriaAtual);

            if (!categoriasNoJson.Contains(nomeCategoriaAtual, StringComparer.OrdinalIgnoreCase)) continue;

            string[] subPastasExtensoes = Directory.GetDirectories(caminhoCategoriaAtual);

            foreach (string caminhoSubPasta in subPastasExtensoes)
            {
                string nomeExtensao = Path.GetFileName(caminhoSubPasta).ToLower();

                string categoriaCorreta = configAtual.Categorias
                    .FirstOrDefault(c => c.Value.ContainsKey(nomeExtensao)).Key;

                if (!string.IsNullOrEmpty(categoriaCorreta) &&
                    !categoriaCorreta.Equals(nomeCategoriaAtual, StringComparison.OrdinalIgnoreCase))
                {
                    string novaPastaPai = Path.Combine(pastaRaiz, categoriaCorreta);
                    if (!Directory.Exists(novaPastaPai)) Directory.CreateDirectory(novaPastaPai);

                    string destinoFinal = Path.Combine(novaPastaPai, nomeExtensao);

                    try
                    {
                        if (!Directory.Exists(destinoFinal))
                        {
                            Directory.Move(caminhoSubPasta, destinoFinal);
                        }
                        else
                        {
                            MoverConteudoInterno(caminhoSubPasta, destinoFinal);
                            if (!Directory.EnumerateFileSystemEntries(caminhoSubPasta).Any())
                                Directory.Delete(caminhoSubPasta, false);
                        }
                    }
                    catch (Exception ex)
                    {
                        RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"),
                            $"Erro ao re-categorizar '{nomeExtensao}' de '{nomeCategoriaAtual}' para '{categoriaCorreta}': {ex.Message}");
                    }
                }
            }
        }
    }

    public static void OrganizarPastasPorPalavraChave(string pastaRaiz)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz)) return;

        // Pega arquivos e pastas soltos na raiz
        var arquivos = Directory.GetFiles(pastaRaiz, "*.*", SearchOption.TopDirectoryOnly);
        var pastas = Directory.GetDirectories(pastaRaiz, "*", SearchOption.TopDirectoryOnly);
        
        if (arquivos.Length == 0 && pastas.Length == 0) return;

        var mapaPalavras = new Dictionary<string, List<string>>();
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "de", "do", "da", "dos", "das", "em", "no", "na", "nos", "nas", 
            "por", "para", "com", "sem", "um", "uma", "uns", "umas", "os", "as", "ao", "aos",
            "que", "como", "sobre"
        };

        var todosItens = arquivos.Concat(pastas).ToList();

        foreach (var item in todosItens)
        {
            // Diferencia extração de nome caso seja arquivo (tira extensão) ou pasta
            string nomeBase = File.Exists(item) ? Path.GetFileNameWithoutExtension(item) : Path.GetFileName(item);
            string nomeLimpo = LimparTexto(nomeBase);
            
            var palavras = nomeLimpo.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Distinct();

            foreach (var palavra in palavras)
            {
                if (palavra.Length <= 2 || stopWords.Contains(palavra)) continue;

                if (!mapaPalavras.ContainsKey(palavra))
                    mapaPalavras[palavra] = new List<string>();

                mapaPalavras[palavra].Add(item);
            }
        }

        var gruposOrdenados = mapaPalavras
            .Where(g => g.Value.Count >= 3)
            .OrderByDescending(g => g.Value.Count)
            .ToList();

        var itensMovidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var grupo in gruposOrdenados)
        {
            string palavraChave = grupo.Key;
            
            var itensDoGrupo = grupo.Value.Where(a => !itensMovidos.Contains(a)).ToList();

            if (itensDoGrupo.Count >= 3)
            {
                string nomePasta = char.ToUpper(palavraChave[0]) + palavraChave.Substring(1);
                string caminhoNovaPasta = Path.Combine(pastaRaiz, nomePasta);

                if (!Directory.Exists(caminhoNovaPasta))
                    Directory.CreateDirectory(caminhoNovaPasta);

                foreach (var item in itensDoGrupo)
                {
                    // Proteção para não mover a nova pasta gerada para dentro dela mesma
                    if (item.Equals(caminhoNovaPasta, StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        string nomeItem = Path.GetFileName(item);
                        string destino = Path.Combine(caminhoNovaPasta, nomeItem);

                        if (File.Exists(item)) // Se for arquivo
                        {
                            if (!File.Exists(destino))
                            {
                                File.Move(item, destino);
                            }
                            else
                            {
                                DateTime dataOrigem = File.GetLastWriteTime(item);
                                DateTime dataDestino = File.GetLastWriteTime(destino);

                                if (dataOrigem > dataDestino)
                                    File.Move(item, destino, true);
                                else
                                    File.Delete(item);
                            }
                        }
                        else if (Directory.Exists(item)) // Se for pasta
                        {
                            if (!Directory.Exists(destino))
                            {
                                Directory.Move(item, destino);
                            }
                            else
                            {
                                MoverConteudoInterno(item, destino);
                                if (!Directory.EnumerateFileSystemEntries(item).Any())
                                    Directory.Delete(item, false);
                            }
                        }
                        
                        itensMovidos.Add(item);
                    }
                    catch { }
                }
            }
        }
    }
    public static void OrganizarPastasPorAnoMes(string pastaRaiz)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz)) return;

        var arquivos = Directory.GetFiles(pastaRaiz, "*.*", SearchOption.TopDirectoryOnly);
        if (arquivos.Length == 0) return;

        // Padrão Regex para encontrar datas no formato YYYYMMDD ou YYYYMM logo após um sublinhado (padrão de Scanner)
        var regexData = new Regex(@"_(20[1-9][0-9])([0-1][0-9])([0-3][0-9])?");

        foreach (var caminhoArquivo in arquivos)
        {
            // Pula nossos logs de sistema
            string nomeArquivo = Path.GetFileName(caminhoArquivo);
            if (nomeArquivo.Equals("extensoes_desconhecidas.txt", StringComparison.OrdinalIgnoreCase) ||
                nomeArquivo.Equals("erros_organizacao.txt", StringComparison.OrdinalIgnoreCase))
                continue;

            string pastaDestino = string.Empty;
            
            // 1. Tenta extrair o Ano e o Mês direto do NOME do arquivo usando Regex
            Match match = regexData.Match(nomeArquivo);
            
            if (match.Success)
            {
                string ano = match.Groups[1].Value;
                string mes = match.Groups[2].Value;
                pastaDestino = $"{ano}_{mes}";
            }
            else
            {
                // 2. Fallback: Se não tem data no nome, pega a data de modificação real do arquivo no sistema
                DateTime dataModificacao = File.GetLastWriteTime(caminhoArquivo);
                pastaDestino = dataModificacao.ToString("yyyy_MM");
            }

            try
            {
                string caminhoNovaPasta = Path.Combine(pastaRaiz, pastaDestino);

                if (!Directory.Exists(caminhoNovaPasta))
                    Directory.CreateDirectory(caminhoNovaPasta);

                string destinoFinal = Path.Combine(caminhoNovaPasta, nomeArquivo);

                if (!File.Exists(destinoFinal))
                {
                    File.Move(caminhoArquivo, destinoFinal);
                }
                else
                {
                    // Proteção de colisão: Verifica a data
                    DateTime dataOrigem = File.GetLastWriteTime(caminhoArquivo);
                    DateTime dataDestinoExistente = File.GetLastWriteTime(destinoFinal);

                    if (dataOrigem > dataDestinoExistente)
                        File.Move(caminhoArquivo, destinoFinal, true);
                    else
                        File.Delete(caminhoArquivo);
                }
            }
            catch (Exception ex)
            {
                RegistrarLogGeral(Path.Combine(pastaRaiz, "erros_organizacao.txt"), $"Erro ao mover '{nomeArquivo}' por Ano/Mês: {ex.Message}");
            }
        }
    }

    private static string LimparTexto(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

            // NOVO: Insere um espaço entre letras e números (e vice-versa)
            // Isso transforma "CCIR123" em "CCIR 123"
            texto = Regex.Replace(texto, @"(?<=[a-zA-Z])(?=\d)|(?<=\d)(?=[a-zA-Z])", " ");

            var textoNormalizado = texto.Normalize(NormalizationForm.FormD);
            var construtor = new StringBuilder();

            foreach (var c in textoNormalizado)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    construtor.Append(c);
            }

            string semAcentos = construtor.ToString().Normalize(NormalizationForm.FormC).ToLower();
            
            // Remove tudo que não for letra de a-z ou número, substituindo por espaço
            return Regex.Replace(semAcentos, @"[^a-z0-9]", " ");
        }

    private static (string Extensao, string Categoria) ObterExtensaoECategoriaDominante(string caminhoPasta, ConfigModel configAtual)
    {
        var contagemExtensoes = new Dictionary<string, int>();

        try
        {
            List<string> arquivos = ObterArquivosSeguros(caminhoPasta);
            if (arquivos.Count == 0) return (string.Empty, string.Empty);

            foreach (string arq in arquivos)
            {
                string ext = Path.GetExtension(arq).Replace(".", "").ToLower();
                if (string.IsNullOrWhiteSpace(ext)) ext = "sem_extensao";

                if (contagemExtensoes.ContainsKey(ext)) contagemExtensoes[ext]++;
                else contagemExtensoes[ext] = 1;
            }

            if (contagemExtensoes.Count > 0)
            {
                string extensaoDominante = contagemExtensoes.OrderByDescending(x => x.Value).First().Key;
                string categoriaEncontrada = configAtual.Categorias
                    .FirstOrDefault(c => c.Value.ContainsKey(extensaoDominante)).Key;

                return (extensaoDominante, categoriaEncontrada ?? string.Empty);
            }
        }
        catch { }
        return (string.Empty, string.Empty);
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

    public static void OrganizarPastasPorCategoria(string pastaRaiz)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz)) return;
        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        string caminhoArquivoJson = Path.Combine(pastaAppData, "extensoes_config.json");
        if (!File.Exists(caminhoArquivoJson)) return;
        ConfigModel configAtual;
        try { configAtual = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(caminhoArquivoJson)) ?? new ConfigModel(); }
        catch { return; }

        foreach (string caminhoPasta in Directory.GetDirectories(pastaRaiz))
        {
            string nomePasta = Path.GetFileName(caminhoPasta);
            if (configAtual.Categorias.Keys.Contains(nomePasta, StringComparer.OrdinalIgnoreCase)) continue;
            string catDest = configAtual.Categorias.FirstOrDefault(c => c.Value.Keys.Contains(nomePasta.ToLower())).Key;
            if (!string.IsNullOrEmpty(catDest))
            {
                string pCat = Path.Combine(pastaRaiz, catDest);
                if (!Directory.Exists(pCat)) Directory.CreateDirectory(pCat);
                string dFinal = Path.Combine(pCat, nomePasta);
                try
                {
                    if (!Directory.Exists(dFinal)) Directory.Move(caminhoPasta, dFinal);
                    else { MoverConteudoInterno(caminhoPasta, dFinal); if (!Directory.EnumerateFileSystemEntries(caminhoPasta).Any()) Directory.Delete(caminhoPasta, false); }
                }
                catch { }
            }
        }
    }

    private static void MoverConteudoInterno(string origem, string destino)
    {
        foreach (var arq in Directory.GetFiles(origem))
        {
            string d = Path.Combine(destino, Path.GetFileName(arq));
            if (!File.Exists(d))
            {
                File.Move(arq, d);
            }
            else
            {
                DateTime dataOrigem = File.GetLastWriteTime(arq);
                DateTime dataDestino = File.GetLastWriteTime(d);
                if (dataOrigem > dataDestino) File.Move(arq, d, true);
                else File.Delete(arq);
            }
        }
        foreach (var sub in Directory.GetDirectories(origem))
        {
            string ds = Path.Combine(destino, Path.GetFileName(sub));
            if (!Directory.Exists(ds)) Directory.Move(sub, ds);
            else
            {
                MoverConteudoInterno(sub, ds);
                if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub, false);
            }
        }
    }

    private static bool EhArquivoShapefile(string nomeArquivo, out string nomeBase)
    {
        nomeBase = string.Empty;
        string n = nomeArquivo.ToLower();
        if (n.EndsWith(".shp.xml")) { nomeBase = nomeArquivo.Substring(0, nomeArquivo.Length - 8); return true; }
        foreach (var ext in ExtensoesShapefile) { if (n.EndsWith(ext)) { nomeBase = nomeArquivo.Substring(0, nomeArquivo.Length - ext.Length); return true; } }
        return false;
    }

public static void OrganizarPdfsPorLayoutVisual(string pastaRaiz, Action<string, bool, string> reportarProgresso = null)
    {
        var arquivosPdfSoltos = Directory.GetFiles(pastaRaiz, "*.pdf", SearchOption.TopDirectoryOnly);
        if (arquivosPdfSoltos.Length == 0) return;

        System.Diagnostics.Debug.WriteLine($"[VISUAL] Encontrados {arquivosPdfSoltos.Length} novos PDFs soltos para categorizar.");

        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        if (!Directory.Exists(pastaAppData)) Directory.CreateDirectory(pastaAppData);
        string caminhoCache = Path.Combine(pastaAppData, "visual_cache.json");

        var cacheVisual = new Dictionary<string, ulong>();
        if (File.Exists(caminhoCache))
        {
            try
            {
                string jsonCache = File.ReadAllText(caminhoCache);
                cacheVisual = JsonSerializer.Deserialize<Dictionary<string, ulong>>(jsonCache) ?? new Dictionary<string, ulong>();
            }
            catch { }
        }

        var algoritmoHash = new DifferenceHash();
        bool cacheAlterado = false;

        // Função interna para processar o Hash Visual
        ulong ObterHashVisual(string caminhoPdf)
        {
            reportarProgresso?.Invoke(caminhoPdf, true, "Calculando Hash Visual...");

            var info = new FileInfo(caminhoPdf);
            string chaveCache = $"{info.Name}_{info.Length}";
            ulong hashResult = 0;

            if (cacheVisual.TryGetValue(chaveCache, out ulong hashSalvo))
            {
                hashResult = hashSalvo;
            }
            else
            {
                try
                {
                    using (var documentoPdf = UglyToad.PdfPig.PdfDocument.Open(caminhoPdf))
                    {
                        var imagens = documentoPdf.GetPage(1).GetImages().ToList();
                        if (imagens.Count > 0)
                        {
                            var imagemPrincipal = imagens.OrderByDescending(img => img.RawBytes.Count).First();
                            using (var imgSharp = Image.Load<Rgba32>(imagemPrincipal.RawBytes.ToArray()))
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

            reportarProgresso?.Invoke(caminhoPdf, false, hashResult != 0 ? "Hash Concluído" : "Sem Imagem (Ignorado)");
            return hashResult;
        }

        // --- 1. PREPARAÇÃO DA FILA E DOS TEMPLATES ---
        var filaDeArquivos = new List<(string Caminho, ulong Hash)>();
        foreach (var pdf in arquivosPdfSoltos)
        {
            ulong hashAtual = ObterHashVisual(pdf);
            if (hashAtual != 0) filaDeArquivos.Add((pdf, hashAtual));
        }

        var templatesPastas = new Dictionary<string, ulong>();
        var subpastas = Directory.GetDirectories(pastaRaiz);
        
        foreach (var subpasta in subpastas)
        {
            var pdfExemplo = Directory.GetFiles(subpasta, "*.pdf", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (pdfExemplo != null)
            {
                ulong hashTemplate = ObterHashVisual(pdfExemplo);
                if (hashTemplate != 0) templatesPastas[subpasta] = hashTemplate;
            }
        }

        // --- 2. ONDAS DE PROCESSAMENTO (Multi-pass descendo até 50%) ---
        // 85% = Idêntico | 75% = Mesmo layout, pequeno desvio | 65% = Ruído/torto | 50% = Máximo de flexibilidade estrutural
        double[] ondasDeTolerancia = { 85.0, 75.0, 65.0, 50.0 };

        foreach (double taxaCorte in ondasDeTolerancia)
        {
            if (filaDeArquivos.Count == 0) break; 

            System.Diagnostics.Debug.WriteLine($"[VISUAL] Iniciando Onda com Tolerância de {taxaCorte}%...");

            var arquivosQueSobraram = new List<(string Caminho, ulong Hash)>();

            // ETAPA A: Tentar encaixar nas pastas que já existem
            foreach (var doc in filaDeArquivos)
            {
                bool encontrouPastaExistente = false;

                foreach (var template in templatesPastas)
                {
                    if (CompareHash.Similarity(doc.Hash, template.Value) >= taxaCorte)
                    {
                        try
                        {
                            string destino = Path.Combine(template.Key, Path.GetFileName(doc.Caminho));
                            if (!File.Exists(destino)) File.Move(doc.Caminho, destino);
                            encontrouPastaExistente = true;
                            break;
                        }
                        catch { }
                    }
                }

                if (!encontrouPastaExistente)
                {
                    arquivosQueSobraram.Add(doc);
                }
            }

            // ETAPA B: Tentar formar novos grupos entre os que sobraram
            var clusters = new List<List<(string Caminho, ulong Hash)>>();
            
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
                if (!adicionado) clusters.Add(new List<(string Caminho, ulong Hash)> { doc });
            }

            filaDeArquivos.Clear(); 

            // ETAPA C: Criar pastas para os novos grupos (Com extração de Título Inteligente via Tesseract)
            foreach (var cluster in clusters)
            {
                if (cluster.Count < 2) 
                {
                    filaDeArquivos.AddRange(cluster);
                    continue; 
                }

                // Extrai a frase com maior fonte do arquivo representante do cluster
                string tituloExtraido = ObterTituloMaiorFonte(cluster[0].Caminho);
                string sufixoTaxa = $"{taxaCorte}%";
                
                string nomeBaseCategoria = string.IsNullOrWhiteSpace(tituloExtraido) 
                                           ? sufixoTaxa 
                                           : $"{tituloExtraido}_{sufixoTaxa}";

                // Garante que o nome da pasta será único (caso já exista uma com o mesmo título e mesma taxa)
                string nomeCategoria = nomeBaseCategoria;
                int contador = 1;
                string pastaDestino = Path.Combine(pastaRaiz, nomeCategoria);
                
                while (Directory.Exists(pastaDestino))
                {
                    nomeCategoria = $"{nomeBaseCategoria} ({contador})";
                    pastaDestino = Path.Combine(pastaRaiz, nomeCategoria);
                    contador++;
                }

                Directory.CreateDirectory(pastaDestino);
                templatesPastas[pastaDestino] = cluster[0].Hash; // Salva como molde para os próximos

                foreach (var doc in cluster)
                {
                    try
                    {
                        string destinoFinal = Path.Combine(pastaDestino, Path.GetFileName(doc.Caminho));
                        if (!File.Exists(destinoFinal)) File.Move(doc.Caminho, destinoFinal);
                    }
                    catch { }
                }
            }
        }

        // --- 3. SALVAMENTO DO CACHE ---
        if (cacheAlterado)
        {
            try
            {
                File.WriteAllText(caminhoCache, JsonSerializer.Serialize(cacheVisual));
                System.Diagnostics.Debug.WriteLine("[VISUAL] Cache salvo com sucesso.");
            }
            catch { }
        }

        System.Diagnostics.Debug.WriteLine($"[VISUAL] Processo concluído. Ficaram {filaDeArquivos.Count} arquivos totalmente únicos na raiz.");
    }

    // ==============================================================================
    // NOVOS MÉTODOS AUXILIARES: Coloque-os logo abaixo do método acima
    // ==============================================================================

private static string ObterTituloMaiorFonte(string caminhoPdf)
    {
        string tituloEncontrado = string.Empty;
        int maiorAlturaEncontrada = 0;

        try
        {
            using (var pdf = UglyToad.PdfPig.PdfDocument.Open(caminhoPdf))
            {
                var page = pdf.GetPage(1);

                // 1. Primeiro tenta extrair por texto nativo do PDF (muito mais rápido)
                // CORREÇÃO: Usa a propriedade 'Letters' em vez do método 'GetLetters()'
                var letras = page.Letters.ToList(); 
                
                if (letras.Count > 0)
                {
                    // Pega o caractere com a maior fonte
                    var maiorLetra = letras.OrderByDescending(l => l.PointSize).First();
                    
                    // Pega todas as letras que estão mais ou menos na mesma altura (Y) e tamanho
                    // CORREÇÃO: Usando GlyphRectangle para pegar o posicionamento exato da letra na página
                    var letrasDoTitulo = letras.Where(l => 
                        Math.Abs(l.GlyphRectangle.Bottom - maiorLetra.GlyphRectangle.Bottom) < maiorLetra.PointSize && 
                        Math.Abs(l.PointSize - maiorLetra.PointSize) < 2)
                        .OrderBy(l => l.GlyphRectangle.Left); // Ordena da esquerda para a direita

                    tituloEncontrado = string.Join("", letrasDoTitulo.Select(l => l.Value)).Trim();
                    
                    if (!string.IsNullOrWhiteSpace(tituloEncontrado) && tituloEncontrado.Length > 2)
                        return LimparNomePasta(tituloEncontrado);
                }

                // 2. Se não tem texto nativo (é scan/imagem), usa o iterador avançado do Tesseract
                var imagens = page.GetImages().ToList();
                if (imagens.Count > 0)
                {
                    var imagemBase = imagens.OrderByDescending(i => i.RawBytes.Count).First();
                    byte[] imgBytes = imagemBase.RawBytes.ToArray();

                    string tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
                    if (Directory.Exists(tessDataPath))
                    {
                        using (var engine = new TesseractEngine(tessDataPath, "por", EngineMode.Default))
                        using (var pix = Pix.LoadFromMemory(imgBytes))
                        using (var tesseractPage = engine.Process(pix))
                        using (var iter = tesseractPage.GetIterator()) // Inicia o iterador espacial
                        {
                            iter.Begin();
                            do
                            {
                                // Analisa a página linha por linha buscando as dimensões do texto
                                if (iter.TryGetBoundingBox(PageIteratorLevel.TextLine, out Rect bounds))
                                {
                                    if (bounds.Height > maiorAlturaEncontrada)
                                    {
                                        string textoLinha = iter.GetText(PageIteratorLevel.TextLine);
                                        
                                        // Garante que não é um ruído de 1 letra só
                                        if (!string.IsNullOrWhiteSpace(textoLinha) && textoLinha.Trim().Length > 3)
                                        {
                                            maiorAlturaEncontrada = bounds.Height;
                                            tituloEncontrado = textoLinha;
                                        }
                                    }
                                }
                            } while (iter.Next(PageIteratorLevel.TextLine));
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

        // Remove caracteres que o Windows não aceita em nomes de pastas
        var invalidChars = Path.GetInvalidFileNameChars();
        string textoLimpo = new string(texto.Where(c => !invalidChars.Contains(c)).ToArray());
        
        // Remove quebras de linha e múltiplos espaços
        textoLimpo = Regex.Replace(textoLimpo, @"\s+", " ").Trim();
        
        // Limita a 40 caracteres para a pasta não ficar gigante e quebrar o limite de caminho do Windows
        if (textoLimpo.Length > 40) textoLimpo = textoLimpo.Substring(0, 40).Trim();
        
        // Capitaliza a primeira letra de cada palavra (Title Case) para ficar esteticamente bonito
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(textoLimpo.ToLower());
    }
}