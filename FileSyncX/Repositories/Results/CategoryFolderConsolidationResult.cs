namespace FileSyncX.Repositories.Results;

public sealed record CategoryFolderConsolidationResult(
    int ParesEncontrados,
    int ParesConsolidados,
    int ParesIgnorados,
    int ArquivosMovidos,
    int DocumentosAtualizados,
    IReadOnlyList<string> Erros)
{
    /// <summary>
    /// Ocorrências que não representam falha de execução, mas explicam
    /// por que determinado par não foi processado ou por que restou conteúdo.
    /// </summary>
    public IReadOnlyList<string> Avisos { get; init; } = [];
}
