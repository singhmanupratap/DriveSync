namespace WebUI.Models;

public class AdminConfigurationViewModel
{
    public HostOrchestrationConfig HostOrchestration { get; set; } = new();
    public FileIndexerConfigViewModel FileIndexer { get; set; } = new();
    public string CurrentHost { get; set; } = Environment.MachineName;
    public List<string> AvailableTimezones { get; set; } = new();
    public bool ConfigurationSaved { get; set; }
    public string? ErrorMessage { get; set; }
}

public class HostOrchestrationConfig
{
    public Dictionary<string, HostConfig> Hosts { get; set; } = new();
    public int BufferTimeMinutes { get; set; } = 3;
    public Dictionary<string, ServiceConfig> Services { get; set; } = new();
}

public class HostConfig
{
    public string Timezone { get; set; } = string.Empty;
    public List<int> ExecutionMinutes { get; set; } = new();
    public string Location { get; set; } = string.Empty;
}

public class ServiceConfig
{
    public int IntervalMinutes { get; set; }
}

public class FileIndexerConfigViewModel
{
    public Dictionary<string, HostSpecificConfigViewModel> Hosts { get; set; } = new();
}

public class HostSpecificConfigViewModel
{
    public bool Enabled { get; set; } = true;
    public int ScanIntervalMinutes { get; set; } = 20;
    public int MaxFilesPerBatch { get; set; } = 1000;
    public string Timezone { get; set; } = string.Empty;
}

public class SaveConfigurationRequest
{
    public HostOrchestrationConfig HostOrchestration { get; set; } = new();
    public FileIndexerConfigViewModel FileIndexer { get; set; } = new();
}