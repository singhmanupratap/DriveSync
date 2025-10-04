using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using WebUI.Models;
using Microsoft.Extensions.Configuration;
using Shared.Services;

namespace WebUI.Controllers;

public class AdminController : Controller
{
    private readonly ILogger<AdminController> _logger;
    private readonly IConfiguration _configuration;
    private readonly IHostOrchestrationService? _hostOrchestrationService;
    private readonly string _hostOrchestrationConfigPath;
    private readonly string _appSettingsPath;

    public AdminController(
        ILogger<AdminController> logger, 
        IConfiguration configuration,
        IHostOrchestrationService? hostOrchestrationService = null)
    {
        _logger = logger;
        _configuration = configuration;
        _hostOrchestrationService = hostOrchestrationService;
        
        // Determine config file paths
        var contentRoot = Directory.GetCurrentDirectory();
        _hostOrchestrationConfigPath = Path.Combine(contentRoot, "host-orchestration-config.json");
        _appSettingsPath = Path.Combine(contentRoot, "appsettings.json");
    }

    public async Task<IActionResult> Index()
    {
        var model = new AdminConfigurationViewModel
        {
            CurrentHost = Environment.MachineName,
            AvailableTimezones = GetAvailableTimezones()
        };

        try
        {
            // Load host orchestration config
            model.HostOrchestration = await LoadHostOrchestrationConfigAsync();
            
            // Load FileIndexer config
            model.FileIndexer = LoadFileIndexerConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading configuration");
            model.ErrorMessage = $"Error loading configuration: {ex.Message}";
        }

        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> SaveConfiguration([FromBody] SaveConfigurationRequest request)
    {
        try
        {
            // Save host orchestration config
            await SaveHostOrchestrationConfigAsync(request.HostOrchestration);
            
            // Save FileIndexer config to appsettings.json
            await SaveFileIndexerConfigAsync(request.FileIndexer);

            _logger.LogInformation("Configuration saved successfully");
            return Json(new { success = true, message = "Configuration saved successfully!" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
            return Json(new { success = false, message = $"Error saving configuration: {ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetHostStatus()
    {
        var hostStatuses = new List<object>();
        
        try
        {
            _logger.LogInformation("GetHostStatus called");
            var config = await LoadHostOrchestrationConfigAsync();
            _logger.LogInformation("Loaded config with {HostCount} hosts", config.Hosts.Count);
            
            foreach (var host in config.Hosts)
            {
                _logger.LogInformation("Processing host: {HostName}", host.Key);
                
                var canStart = _hostOrchestrationService != null 
                    ? await _hostOrchestrationService.CanServiceStartNowAsync("FileIndexer")
                    : false;
                
                var nextExecution = GetNextExecutionTime(host.Value);
                
                // Ensure ExecutionMinutes is never null
                var executionMinutes = host.Value.ExecutionMinutes ?? new List<int>();
                
                var hostStatus = new
                {
                    HostName = host.Key,
                    Location = host.Value.Location ?? string.Empty,
                    Timezone = host.Value.Timezone ?? string.Empty,
                    CanStart = canStart,
                    NextExecution = nextExecution?.ToString("yyyy-MM-dd HH:mm:ss"),
                    ExecutionMinutes = executionMinutes,
                    IsCurrentHost = string.Equals(host.Key, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                };
                
                _logger.LogInformation("Host {HostName}: Location={Location}, Timezone={Timezone}, ExecutionMinutes={ExecutionMinutes}, IsCurrentHost={IsCurrentHost}", 
                    hostStatus.HostName, hostStatus.Location, hostStatus.Timezone, 
                    string.Join(",", executionMinutes), hostStatus.IsCurrentHost);
                
                hostStatuses.Add(hostStatus);
            }
            
            _logger.LogInformation("Returning {Count} host statuses", hostStatuses.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting host status");
            // Return a safe empty response
            return Json(new List<object>());
        }

        return Json(hostStatuses);
    }

    [HttpPost]
    public async Task<IActionResult> TestHostConfiguration()
    {
        try
        {
            var config = await LoadHostOrchestrationConfigAsync();
            var validationResults = new List<object>();

            foreach (var host in config.Hosts)
            {
                var validation = ValidateHostConfig(host.Key, host.Value);
                validationResults.Add(new
                {
                    HostName = host.Key,
                    IsValid = validation.IsValid,
                    Issues = validation.Issues,
                    NextExecutions = GetNext5ExecutionTimes(host.Value)
                });
            }

            return Json(new { success = true, results = validationResults });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing host configuration");
            return Json(new { success = false, message = ex.Message });
        }
    }

    private async Task<HostOrchestrationConfig> LoadHostOrchestrationConfigAsync()
    {
        if (!System.IO.File.Exists(_hostOrchestrationConfigPath))
        {
            _logger.LogWarning("Host orchestration config file not found at {Path}, creating default", _hostOrchestrationConfigPath);
            return CreateDefaultHostOrchestrationConfig();
        }

        try
        {
            var json = await System.IO.File.ReadAllTextAsync(_hostOrchestrationConfigPath);
            _logger.LogDebug("Loaded config JSON: {Json}", json);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            
            var rootConfig = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, options);
            
            if (rootConfig?.ContainsKey("HostOrchestration") == true)
            {
                var hostOrchestrationElement = rootConfig["HostOrchestration"];
                var hostOrchestrationConfig = JsonSerializer.Deserialize<HostOrchestrationConfig>(hostOrchestrationElement.GetRawText(), options);
                
                if (hostOrchestrationConfig != null)
                {
                    // Ensure all ExecutionMinutes lists are initialized
                    foreach (var host in hostOrchestrationConfig.Hosts.Values)
                    {
                        host.ExecutionMinutes ??= new List<int>();
                    }
                    
                    _logger.LogDebug("Successfully loaded host orchestration config with {HostCount} hosts", hostOrchestrationConfig.Hosts.Count);
                    return hostOrchestrationConfig;
                }
            }
            
            _logger.LogWarning("Invalid config structure, creating default");
            return CreateDefaultHostOrchestrationConfig();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading host orchestration config from {Path}", _hostOrchestrationConfigPath);
            return CreateDefaultHostOrchestrationConfig();
        }
    }

    private FileIndexerConfigViewModel LoadFileIndexerConfig()
    {
        var config = new FileIndexerConfigViewModel();
        var fileIndexerSection = _configuration.GetSection("FileIndexer:Hosts");
        
        if (fileIndexerSection.Exists())
        {
            foreach (var hostSection in fileIndexerSection.GetChildren())
            {
                var hostConfig = new HostSpecificConfigViewModel();
                hostSection.Bind(hostConfig);
                config.Hosts[hostSection.Key] = hostConfig;
            }
        }
        else
        {
            // Create default config for known hosts
            var defaultHosts = new[] { "HOME-DESKTOP", "LENOVO-LAPTOP", "MI_Home" };
            foreach (var host in defaultHosts)
            {
                config.Hosts[host] = new HostSpecificConfigViewModel
                {
                    Enabled = true,
                    ScanIntervalMinutes = 20,
                    MaxFilesPerBatch = 1000,
                    Timezone = host == "HOME-DESKTOP" ? "Asia/Kolkata" : "Europe/Berlin"
                };
            }
        }

        return config;
    }

    private async Task SaveHostOrchestrationConfigAsync(HostOrchestrationConfig config)
    {
        var configWrapper = new { HostOrchestration = config };
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(configWrapper, options);
        await System.IO.File.WriteAllTextAsync(_hostOrchestrationConfigPath, json);
    }

    private async Task SaveFileIndexerConfigAsync(FileIndexerConfigViewModel config)
    {
        // Read existing appsettings.json
        var existingJson = "{}";
        if (System.IO.File.Exists(_appSettingsPath))
        {
            existingJson = await System.IO.File.ReadAllTextAsync(_appSettingsPath);
        }

        var existingConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(existingJson) 
                           ?? new Dictionary<string, object>();

        // Update FileIndexer section
        existingConfig["FileIndexer"] = new { Hosts = config.Hosts };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var updatedJson = JsonSerializer.Serialize(existingConfig, options);
        await System.IO.File.WriteAllTextAsync(_appSettingsPath, updatedJson);
    }

    private HostOrchestrationConfig CreateDefaultHostOrchestrationConfig()
    {
        return new HostOrchestrationConfig
        {
            BufferTimeMinutes = 3,
            Hosts = new Dictionary<string, HostConfig>
            {
                ["HOME-DESKTOP"] = new() 
                { 
                    Timezone = "Asia/Kolkata", 
                    ExecutionMinutes = [0, 20, 40], 
                    Location = "India" 
                },
                ["LENOVO-LAPTOP"] = new() 
                { 
                    Timezone = "Europe/Berlin", 
                    ExecutionMinutes = [8, 28, 48], 
                    Location = "Germany" 
                },
                ["MI_Home"] = new() 
                { 
                    Timezone = "Europe/Berlin", 
                    ExecutionMinutes = [16, 36, 56], 
                    Location = "Germany" 
                }
            },
            Services = new Dictionary<string, ServiceConfig>
            {
                ["FileIndexer"] = new() { IntervalMinutes = 20 },
                ["DeleteInactiveFiles"] = new() { IntervalMinutes = 30 },
                ["SharedToLocalSync"] = new() { IntervalMinutes = 30 },
                ["DatabaseSync"] = new() { IntervalMinutes = 15 }
            }
        };
    }

    private List<string> GetAvailableTimezones()
    {
        return new List<string>
        {
            "Asia/Kolkata",
            "Europe/Berlin",
            "UTC",
            "America/New_York",
            "America/Los_Angeles",
            "Europe/London",
            "Asia/Tokyo",
            "Australia/Sydney"
        };
    }

    private DateTime? GetNextExecutionTime(HostConfig hostConfig)
    {
        try
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(hostConfig.Timezone);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
            
            foreach (var minute in hostConfig.ExecutionMinutes.OrderBy(m => m))
            {
                var nextTime = new DateTime(now.Year, now.Month, now.Day, now.Hour, minute, 0);
                if (nextTime > now)
                    return nextTime;
            }
            
            // Next day, first execution
            var tomorrow = now.AddDays(1).Date;
            return new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day, 0, hostConfig.ExecutionMinutes.Min(), 0);
        }
        catch
        {
            return null;
        }
    }

    private List<string> GetNext5ExecutionTimes(HostConfig hostConfig)
    {
        var times = new List<string>();
        try
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(hostConfig.Timezone);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone);
            var currentDate = now.Date;
            
            for (int day = 0; day < 2 && times.Count < 5; day++)
            {
                var date = currentDate.AddDays(day);
                foreach (var minute in hostConfig.ExecutionMinutes.OrderBy(m => m))
                {
                    var execTime = new DateTime(date.Year, date.Month, date.Day, now.Hour, minute, 0);
                    if (day == 0 && execTime <= now) continue;
                    
                    times.Add(execTime.ToString("yyyy-MM-dd HH:mm"));
                    if (times.Count >= 5) break;
                }
            }
        }
        catch { }
        
        return times;
    }

    private (bool IsValid, List<string> Issues) ValidateHostConfig(string hostName, HostConfig config)
    {
        var issues = new List<string>();
        
        if (string.IsNullOrEmpty(config.Timezone))
            issues.Add("Timezone is required");
        
        if (!config.ExecutionMinutes.Any())
            issues.Add("At least one execution minute must be specified");
        
        if (config.ExecutionMinutes.Any(m => m < 0 || m > 59))
            issues.Add("Execution minutes must be between 0 and 59");
        
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(config.Timezone);
        }
        catch
        {
            issues.Add($"Invalid timezone: {config.Timezone}");
        }

        return (issues.Count == 0, issues);
    }
}