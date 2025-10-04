using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Services;

public class HostOrchestrationService : IHostOrchestrationService
{
    private readonly ILogger<HostOrchestrationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _hostName;
    
    // Define execution offsets for each host (in minutes) with adequate buffer time
    private readonly Dictionary<string, Dictionary<string, int>> _hostOffsets = new()
    {
        ["HOME-DESKTOP"] = new()
        {
            ["FileIndexer"] = 0,      // Runs at 0, 20, 40 (every 20 min)
            ["DeleteInactive"] = 2,   // Runs at 2, 32 (every 30 min, 2min buffer after FileIndexer)
            ["SharedToLocal"] = 5,    // Runs at 5, 35 (every 30 min, 5min buffer)
            ["DatabaseSync"] = 1      // Runs every 15 min starting at 1 (1, 16, 31, 46)
        },
        ["LENOVO-LAPTOP"] = new()
        {
            ["FileIndexer"] = 8,      // Runs at 8, 28, 48 (every 20 min, 8min buffer after HOME-DESKTOP)
            ["DeleteInactive"] = 12,  // Runs at 12, 42 (every 30 min, 4min buffer after FileIndexer)
            ["SharedToLocal"] = 15,   // Runs at 15, 45 (every 30 min, 3min buffer)
            ["DatabaseSync"] = 6      // Runs every 15 min starting at 6 (6, 21, 36, 51)
        },
        ["MI_Home"] = new()
        {
            ["FileIndexer"] = 16,     // Runs at 16, 36, 56 (every 20 min, 8min buffer after LENOVO-LAPTOP)
            ["DeleteInactive"] = 22,  // Runs at 22, 52 (every 30 min, 6min buffer after FileIndexer)
            ["SharedToLocal"] = 25,   // Runs at 25, 55 (every 30 min, 3min buffer)
            ["DatabaseSync"] = 11     // Runs every 15 min starting at 11 (11, 26, 41, 56)
        }
    };

    // Define minimum buffer time between operations (in minutes)
    private readonly int _minBufferTimeMinutes = 3;

    public HostOrchestrationService(
        ILogger<HostOrchestrationService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _hostName = Environment.MachineName;
    }

    public async Task<TimeSpan> GetStartupDelayAsync(string serviceName)
    {
        if (!_hostOffsets.TryGetValue(_hostName, out var hostConfig) ||
            !hostConfig.TryGetValue(serviceName, out var offsetMinutes))
        {
            _logger.LogWarning("No offset configuration found for host {HostName} and service {ServiceName}", _hostName, serviceName);
            return TimeSpan.Zero;
        }

        var now = DateTime.Now;
        var currentMinute = now.Minute;
        
        // Calculate the next execution time based on the offset
        var nextExecutionMinute = offsetMinutes;
        while (nextExecutionMinute <= currentMinute)
        {
            nextExecutionMinute += GetServiceBaseInterval(serviceName);
        }
        
        // If we've gone past 59 minutes, wrap to next hour
        if (nextExecutionMinute >= 60)
        {
            nextExecutionMinute = offsetMinutes;
            var nextExecution = new DateTime(now.Year, now.Month, now.Day, now.Hour + 1, nextExecutionMinute, 0);
            var delay = nextExecution - now;
            
            _logger.LogInformation("Host {HostName} service {ServiceName} will start in {Delay} at {NextTime}", 
                _hostName, serviceName, delay, nextExecution);
            
            return delay;
        }
        else
        {
            var nextExecution = new DateTime(now.Year, now.Month, now.Day, now.Hour, nextExecutionMinute, 0);
            var delay = nextExecution - now;
            
            _logger.LogInformation("Host {HostName} service {ServiceName} will start in {Delay} at {NextTime}", 
                _hostName, serviceName, delay, nextExecution);
            
            return delay;
        }
    }

    public async Task<int> GetStaggeredIntervalAsync(string serviceName, int baseInterval)
    {
        // Return the base interval as the staggering is handled by startup delay
        return baseInterval;
    }

    public bool IsServiceEnabledForHost(string serviceName)
    {
        var configPath = $"{serviceName}:HostConfigs:{_hostName}:Enabled";
        var enabled = _configuration.GetValue<bool?>(configPath);
        
        if (!enabled.HasValue)
        {
            // Fallback to global enabled setting
            var globalConfigPath = $"{serviceName}:Enabled";
            enabled = _configuration.GetValue<bool?>(globalConfigPath) ?? true;
        }
        
        _logger.LogInformation("Service {ServiceName} enabled for host {HostName}: {Enabled}", 
            serviceName, _hostName, enabled);
        
        return enabled.Value;
    }

    private int GetServiceBaseInterval(string serviceName)
    {
        return serviceName switch
        {
            "FileIndexer" => 20,      // Every 20 minutes with better spacing
            "DeleteInactive" => 30,   // Every 30 minutes
            "SharedToLocal" => 30,    // Every 30 minutes  
            "DatabaseSync" => 15,     // Every 15 minutes
            _ => 20
        };
    }

    public async Task<bool> CanServiceStartNowAsync(string serviceName)
    {
        // Check if any other critical service from other hosts might be running
        var now = DateTime.Now;
        var currentMinute = now.Minute;
        
        // Check for potential conflicts with other hosts
        foreach (var (hostName, hostConfig) in _hostOffsets)
        {
            if (hostName == _hostName) continue; // Skip own host
            
            foreach (var (otherServiceName, otherOffset) in hostConfig)
            {
                if (IsServiceCritical(otherServiceName))
                {
                    var interval = GetServiceBaseInterval(otherServiceName);
                    var otherServiceCurrentSlot = (currentMinute - otherOffset + 60) % interval;
                    
                    // If another critical service started within buffer time, wait
                    if (otherServiceCurrentSlot < _minBufferTimeMinutes)
                    {
                        _logger.LogInformation("Delaying {ServiceName} on {HostName} due to {OtherService} on {OtherHost} running within buffer time", 
                            serviceName, _hostName, otherServiceName, hostName);
                        return false;
                    }
                }
            }
        }
        
        return true;
    }

    private bool IsServiceCritical(string serviceName)
    {
        // Define which services are critical for database access
        return serviceName is "FileIndexer" or "DeleteInactive" or "DatabaseSync";
    }
}

public static class HostOrchestrationExtensions
{
    public static async Task DelayForHostOrchestrationAsync(this BackgroundService service, 
        IHostOrchestrationService orchestration, string serviceName, ILogger logger)
    {
        var delay = await orchestration.GetStartupDelayAsync(serviceName);
        if (delay > TimeSpan.Zero)
        {
            logger.LogInformation("Delaying service {ServiceName} startup by {Delay} for host orchestration", 
                serviceName, delay);
            await Task.Delay(delay);
        }
    }
}