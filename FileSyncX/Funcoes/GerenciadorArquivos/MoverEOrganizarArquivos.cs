using FileSyncX.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Google.GenAI;

namespace FileSyncX.Funcoes.GerenciadorArquivos;

public static class MoverEOrganizarArquivos
{
    private const string GeminiApiKey = "AIzaSyC2E1lXZ5EyX29KEOyC7NUMdmyB6YeWE2M";

    private static readonly string[] ExtensoesShapefile = new[]
    {
        ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sbn", ".sbx",
        ".atx", ".fbn", ".fbx", ".qmd", ".ain", ".aih", ".ixs", ".mxs"
    };

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
            string status = MoverArquivoParaDestino(pastaRaiz, arquivo.CaminhoCompleto, arquivo.Nome, arquivo.Extensao, out string novoC);
            arquivo.StatusSincronizacao = status;
            if (status == "Sincronizado") arquivo.CaminhoCompleto = novoC;
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
            if (!File.Exists(cDest)) { File.Move(caminhoOrigem, cDest); caminhoFinal = cDest; return "Sincronizado"; }
            return "Já Existe";
        }
        catch { return "Erro"; }
    }

    public static async Task OrganizarPastasDesconhecidasComIAAsync(string pastaRaiz)
    {
        System.Diagnostics.Debug.WriteLine($"[IA] Iniciando análise de pastas em: {pastaRaiz}");

        string pastaAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FileSyncX");
        string caminhoArquivoJson = Path.Combine(pastaAppData, "extensoes_config.json");
        if (!File.Exists(caminhoArquivoJson)) return;

        ConfigModel configAtual;
        try { configAtual = JsonSerializer.Deserialize<ConfigModel>(File.ReadAllText(caminhoArquivoJson)) ?? new ConfigModel(); }
        catch { return; }

        string[] diretoriosNaRaiz = Directory.GetDirectories(pastaRaiz);
        var nomesCategorias = configAtual.Categorias.Keys.ToList();

        // Correção: Passando a chave diretamente no construtor do SDK para evitar erros de variável de ambiente
        var client = new Client(apiKey: GeminiApiKey);

        foreach (string caminhoPasta in diretoriosNaRaiz)
        {
            string nomePasta = Path.GetFileName(caminhoPasta);

            if (nomesCategorias.Any(c => c.Equals(nomePasta, StringComparison.OrdinalIgnoreCase))) continue;
            if (configAtual.Categorias.Any(c => c.Value.ContainsKey(nomePasta.ToLower()))) continue;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[IA] Classificando: '{nomePasta}'...");

                string prompt = $"Categorias: {string.Join(", ", nomesCategorias)}. " +
                               $"A qual dessas categorias a pasta '{nomePasta}' melhor se encaixa? " +
                               $"Responda APENAS o nome exato da categoria conforme listado acima.";

                var response = await client.Models.GenerateContentAsync(
                    model: "gemini-3-flash-preview",
                    contents: prompt
                );

                if (response == null || response.Candidates == null || response.Candidates.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[IA] Erro: A API retornou uma resposta vazia ou nula.");
                    continue;
                }

                string respostaIA = response.Candidates[0].Content.Parts[0].Text ?? "";

                System.Diagnostics.Debug.WriteLine($"[IA] Resposta bruta da IA: '{respostaIA}'");

                string respostaLimpa = respostaIA.Replace("\"", "").Replace(".", "").Replace("'", "").Replace("*", "").Trim();

                string? categoriaReal = nomesCategorias.FirstOrDefault(c =>
                    c.Equals(respostaLimpa, StringComparison.OrdinalIgnoreCase) ||
                    c.Replace("_", " ").Equals(respostaLimpa, StringComparison.OrdinalIgnoreCase));

                if (categoriaReal != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[IA] Resultado validado: '{nomePasta}' -> '{categoriaReal}'");
                    string pastaAlvo = Path.Combine(pastaRaiz, categoriaReal);
                    if (!Directory.Exists(pastaAlvo)) Directory.CreateDirectory(pastaAlvo);

                    string destinoFinal = Path.Combine(pastaAlvo, nomePasta);

                    if (!Directory.Exists(destinoFinal))
                    {
                        if (!Directory.EnumerateFileSystemEntries(caminhoPasta).Any())
                        {
                            System.Diagnostics.Debug.WriteLine($"[IA] Removendo pasta '{nomePasta}' pois já estava vazia.");
                            Directory.Delete(caminhoPasta, false);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[IA] Movendo pasta física '{nomePasta}' para '{categoriaReal}'.");
                            Directory.Move(caminhoPasta, destinoFinal);
                        }
                    }
                    else
                    {
                        MoverConteudoInterno(caminhoPasta, destinoFinal);
                        if (!Directory.EnumerateFileSystemEntries(caminhoPasta).Any()) Directory.Delete(caminhoPasta, false);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[IA] A IA sugeriu uma categoria que não foi encontrada no JSON: '{respostaLimpa}'");
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[IA] Falha Crítica ao processar '{nomePasta}': {ex.Message}"); }
        }
        System.Diagnostics.Debug.WriteLine("[IA] Processo finalizado.");
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
            if (!File.Exists(d)) File.Move(arq, d);
        }
        foreach (var sub in Directory.GetDirectories(origem))
        {
            string ds = Path.Combine(destino, Path.GetFileName(sub));
            if (!Directory.Exists(ds)) Directory.Move(sub, ds);
            else { MoverConteudoInterno(sub, ds); if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub, false); }
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
}