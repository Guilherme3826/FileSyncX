namespace FileSyncX.Repositories.Results;

public sealed record SemanticCategoryListItem(
    int Id,
    string CategoryName,
    string SemanticDescription,
    string Keywords,
    string StoredDescription,
    string DestinationFolder,
    int DocumentCount,
    DateTimeOffset CreatedAt);
