namespace Shared.Configuration;

public class FileIndexerConfiguration
{
    public const string SectionName = "FileIndexer";
    
    public string ScanMode { get; set; } = "Auto"; // "Initial", "Incremental", "Auto"
    public int ScanIntervalMinutes { get; set; } = 5;
    public bool ForceInitialScan { get; set; } = false;
    public string TimeZone { get; set; } = "UTC";
    public int BatchSize { get; set; } = 1000; // Progress reporting interval
    public Dictionary<string, HostSpecificConfig> HostConfigs { get; set; } = new();
}

public class HostSpecificConfig
{
    public string ScanMode { get; set; } = "Auto";
    public int ScanIntervalMinutes { get; set; } = 5;
    public bool ForceInitialScan { get; set; } = false;
    public string TimeZone { get; set; } = "UTC";
    public bool Enabled { get; set; } = true;
}

public enum ScanType
{
    Initial,
    Incremental
}