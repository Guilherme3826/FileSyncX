using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace FileSyncX.Funcoes.GerenciadorArquivos;

public static class DeduplicadorArquivos
{
    /// <summary>
    /// Verifica arquivos com sufixos de duplicata do Windows (" - Copia", "(1)", "(2)"), 
    /// mantém o mais recém-modificado e apaga os mais antigos.
    /// </summary>
    /// <param name="pastaRaiz">O diretório onde a busca será feita.</param>
    /// <param name="incluirSubpastas">Se true, vasculha todas as subpastas. Se false, apenas a pasta raiz.</param>
    public static void RemoverDuplicatasMaisAntigas(string pastaRaiz, bool incluirSubpastas = true)
    {
        if (string.IsNullOrWhiteSpace(pastaRaiz) || !Directory.Exists(pastaRaiz))
            return;

        var opcaoBusca = incluirSubpastas ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var arquivos = Directory.GetFiles(pastaRaiz, "*.*", opcaoBusca);

        // Regex atualizada para capturar o "Nome Base".
        // Ignora qualquer repetição no final do arquivo de: " - Copia", " - Copy" ou " (número)"
        var regexDuplicata = new Regex(@"^(.*?)(?:(?:\s-\s(?:Copia|Copy))|(?:\s\(\d+\)))+$", RegexOptions.IgnoreCase);

        // 1. Agrupar os arquivos pela chave: (Diretório + Nome Base + Extensão)
        var grupos = arquivos
            .Select(caminho =>
            {
                var info = new FileInfo(caminho);
                string nomeSemExtensao = Path.GetFileNameWithoutExtension(info.Name);

                var match = regexDuplicata.Match(nomeSemExtensao);
                // Se a Regex der match (achou os sufixos), pega o Grupo 1 (o nome limpo). Senão, é o próprio nome.
                string nomeBase = match.Success ? match.Groups[1].Value.Trim() : nomeSemExtensao;

                return new
                {
                    Caminho = caminho,
                    Diretorio = info.DirectoryName ?? string.Empty,
                    NomeBase = nomeBase,
                    Extensao = info.Extension.ToLower(),
                    DataModificacao = info.LastWriteTime,
                    Info = info
                };
            })
            .GroupBy(x => new { x.Diretorio, x.NomeBase, x.Extensao })
            .Where(g => g.Count() > 1); // Pega apenas grupos que geraram 2 ou mais arquivos na mesma base

        // 2. Processar cada grupo de duplicatas
        foreach (var grupo in grupos)
        {
            // Ordena do mais recente para o mais antigo (mantemos a última modificação no topo)
            var arquivosOrdenados = grupo.OrderByDescending(a => a.DataModificacao).ToList();

            var arquivoParaManter = arquivosOrdenados.First();
            var arquivosParaApagar = arquivosOrdenados.Skip(1).ToList();

            foreach (var arquivoAntigo in arquivosParaApagar)
            {
                try
                {
                    File.Delete(arquivoAntigo.Caminho);
                    System.Diagnostics.Debug.WriteLine($"[Deletado] {arquivoAntigo.Info.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Erro] Falha ao deletar {arquivoAntigo.Info.Name}: {ex.Message}");
                }
            }

            // 3. Restaurar o nome original
            // Se restou apenas o "Nome - Copia (2).xlsx", renomeamos ele de volta para "Nome.xlsx"
            string caminhoBaseEsperado = Path.Combine(arquivoParaManter.Diretorio, arquivoParaManter.NomeBase + arquivoParaManter.Extensao);

            if (arquivoParaManter.Caminho != caminhoBaseEsperado && !File.Exists(caminhoBaseEsperado))
            {
                try
                {
                    File.Move(arquivoParaManter.Caminho, caminhoBaseEsperado);
                    System.Diagnostics.Debug.WriteLine($"[Renomeado] {arquivoParaManter.Info.Name} -> {Path.GetFileName(caminhoBaseEsperado)}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Erro] Falha ao renomear {arquivoParaManter.Info.Name}: {ex.Message}");
                }
            }
        }
    }
}