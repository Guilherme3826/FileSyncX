using FileSyncX.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FileSyncX.Funcoes.GerenciadorArquivos;

public static class MoverEOrganizarArquivos
{
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

    /// <summary>
    /// Nova função: Varre as categorias existentes e re-aloca pastas de extensões caso a regra do JSON tenha mudado.
    /// </summary>
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

            // Só entra em pastas que são de fato categorias reconhecidas
            if (!categoriasNoJson.Contains(nomeCategoriaAtual, StringComparer.OrdinalIgnoreCase)) continue;

            string[] subPastasExtensoes = Directory.GetDirectories(caminhoCategoriaAtual);

            foreach (string caminhoSubPasta in subPastasExtensoes)
            {
                string nomeExtensao = Path.GetFileName(caminhoSubPasta).ToLower();

                // Verifica no JSON a qual categoria essa subpasta (extensão) deveria pertencer hoje
                string categoriaCorreta = configAtual.Categorias
                    .FirstOrDefault(c => c.Value.ContainsKey(nomeExtensao)).Key;

                // Se a categoria correta for diferente da atual, movemos
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
                            // Se já existe no destino, mescla os arquivos respeitando a data mais recente
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
}