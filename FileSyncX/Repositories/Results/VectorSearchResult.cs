namespace FileSyncX.Repositories.Results;

public sealed record VectorSearchResult<T>(
    T Entity,
    double Distance,
    double Similarity
) where T : class;