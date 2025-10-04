namespace Shared.Configuration;

public class SharedToLocalConfiguration
{
    public const string SectionName = "SharedToLocalConfiguration";

    public string SharedFolderPath { get; set; } = string.Empty;
    public string LocalFolderPath { get; set; } = string.Empty;
    public int SyncIntervalMinutes { get; set; } = 5;
    public string ServiceUser { get; set; } = "DriveSyncUser";
}