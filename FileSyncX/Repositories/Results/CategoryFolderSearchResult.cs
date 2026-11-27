namespace FileSyncX.Repositories.Results;

/// <summary>
/// Resultado da análise semântica após validar os destinos no sistema de arquivos.
/// Somente categorias com diretórios físicos acessíveis podem participar dos pares.
/// </summary>
public sealed record CategoryFolderSearchResult(
    IReadOnlyList<CategoryFolderMergeCandidate> Candidatas,
    int CategoriasVerificadas,
    int CategoriasComPastaFisica,
    int CategoriasSemPastaFisica,
    IReadOnlyList<string> CaminhosIndisponiveis);
