
namespace FileSyncX.Repositories.Results;

public sealed record HashDefaultSimilarityResult(
    int Id,
    string Description,
    string DestinationFolder,
    double CosineDistance,
    double Similarity
);
