namespace FileSyncX.EfCore.Models;

public static class ArchiveClassificationStatus
{
    public const string Pending = "pending";

    public const string Classified = "classified";

    public const string Review = "review";

    public const string Unclassified = "unclassified";

    public static bool IsValid(string? status)
    {
        return status is
            Pending or
            Classified or
            Review or
            Unclassified;
    }
}