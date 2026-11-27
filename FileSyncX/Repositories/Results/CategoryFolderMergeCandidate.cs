namespace FileSyncX.Repositories.Results;

/// <summary>
/// Par retornado pela consulta oficial de similaridade entre categorias
/// em pastas distintas. Não representa uma fusão já executada.
/// </summary>
public sealed record CategoryFolderMergeCandidate(
    int IdCategoriaA,
    string CategoriaA,
    int DocumentosA,
    string PastaA,
    int IdCategoriaB,
    string CategoriaB,
    int DocumentosB,
    string PastaB,
    double Similaridade,
    double DistanciaCosseno,
    int IdCategoriaPrincipalSugerida,
    int IdCategoriaSecundariaSugerida,
    string PastaPrincipalSugerida)
{
    public string SimilaridadeFormatada => $"{Similaridade:P2}";

    public string PrincipalSugerida =>
        IdCategoriaPrincipalSugerida == IdCategoriaA
            ? $"{CategoriaA} (ID {IdCategoriaA})"
            : $"{CategoriaB} (ID {IdCategoriaB})";

    public string SecundariaSugerida =>
        IdCategoriaSecundariaSugerida == IdCategoriaA
            ? $"{CategoriaA} (ID {IdCategoriaA})"
            : $"{CategoriaB} (ID {IdCategoriaB})";

    public int DocumentosDaPrincipal =>
        IdCategoriaPrincipalSugerida == IdCategoriaA ? DocumentosA : DocumentosB;

    public int DocumentosDaSecundaria =>
        IdCategoriaSecundariaSugerida == IdCategoriaA ? DocumentosA : DocumentosB;
}
