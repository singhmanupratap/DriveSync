namespace Shared.Models.SharedToLocal;

public class FileChangeInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public FileChangeType ChangeType { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsDeleteOperation { get; set; }
}

public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}