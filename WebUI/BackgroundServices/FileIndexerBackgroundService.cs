using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Configuration;
using Shared.Services;

namespace DriveSync.WebUI.BackgroundServices;

public class FileIndexerBackgroundService : BaseBackgroundService
{
    private readonly ILogger<FileIndexerBackgroundService> _fileIndexerLogger;
    private readonly IConfiguration _configuration;
    private readonly IFileIndexerService _fileIndexerService;
    private readonly IHostOrchestrationService _hostOrchestration;
    private readonly FileIndexerConfiguration _config;
    private readonly string _hostName;
    private readonly int _scanIntervalMinutes;

    public FileIndexerBackgroundService(
        ILogger<FileIndexerBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IFileIndexerService fileIndexerService,
        IHostOrchestrationService hostOrchestration)
        : base(logger, serviceProvider, "FileIndexer")
    {
        _fileIndexerLogger = logger;
        _configuration = configuration;
        _fileIndexerService = fileIndexerService;
        _hostOrchestration = hostOrchestration;
        
        // Initialize configuration
        _config = new FileIndexerConfiguration();
        _configuration.GetSection(FileIndexerConfiguration.SectionName).Bind(_config);
        
        _hostName = Environment.MachineName;
        
        // Get host-specific configuration
        var hostConfig = GetHostSpecificConfig();
        _scanIntervalMinutes = hostConfig.ScanIntervalMinutes;
        
        if (!hostConfig.Enabled)
        {
            _fileIndexerLogger.LogInformation("FileIndexerBackgroundService is disabled for host: {HostName}", _hostName);
        }
    }

    private HostSpecificConfig GetHostSpecificConfig()
    {
        if (_config.HostConfigs.TryGetValue(_hostName, out var hostConfig))
        {
            return hostConfig;
        }
        
        // Return default configuration
        return new HostSpecificConfig
        {
            ScanMode = _config.ScanMode,
            ScanIntervalMinutes = _config.ScanIntervalMinutes,
            ForceInitialScan = _config.ForceInitialScan,
            TimeZone = _config.TimeZone,
            Enabled = true
        };
    }

    protected override async Task ExecuteServiceAsync(CancellationToken stoppingToken)
    {
        var hostConfig = GetHostSpecificConfig();
        
        if (!hostConfig.Enabled)
        {
            _fileIndexerLogger.LogInformation("FileIndexerBackgroundService is disabled for host: {HostName}", _hostName);
            return;
        }

        // Apply host orchestration startup delay
        var startupDelay = await _hostOrchestration.GetStartupDelayAsync("FileIndexer");
        if (startupDelay > TimeSpan.Zero)
        {
            _fileIndexerLogger.LogInformation("Delaying FileIndexer startup by {Delay} for host orchestration on {HostName}", 
                startupDelay, _hostName);
            await Task.Delay(startupDelay, stoppingToken);
        }

        await _fileIndexerService.InitializeAsync();
        
        var scanType = await _fileIndexerService.DetermineScanTypeAsync();
        _fileIndexerLogger.LogInformation("FileIndexerBackgroundService started successfully for host: {HostName} - Scan interval: {ScanInterval} minutes, Initial scan type: {ScanType}", 
            _hostName, _scanIntervalMinutes, scanType);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if we can start now (buffer time validation)
                var canStart = _hostOrchestration != null ? await _hostOrchestration.CanServiceStartNowAsync("FileIndexer") : true;
                if (!canStart)
                {
                    _fileIndexerLogger.LogInformation("Delaying FileIndexer scan due to other host activity - waiting {BufferTime} minutes", 3);
                    await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);
                    continue;
                }

                // Use the base class method for automatic database sync
                await ExecuteWithDatabaseSyncAsync(async () =>
                {
                    _fileIndexerLogger.LogInformation("Starting FileIndexer scan for host: {HostName}", _hostName);
                    await _fileIndexerService.IndexFilesAsync(stoppingToken);
                    await _fileIndexerService.LogStatisticsAsync();
                    _fileIndexerLogger.LogInformation("Completed FileIndexer scan for host: {HostName}", _hostName);
                }, "file indexing scan");

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_scanIntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                // Service is stopping
                break;
            }
            catch (Exception ex)
            {
                _fileIndexerLogger.LogError(ex, "Error during FileIndexer scan for host: {HostName}", _hostName);
                // Continue running, try again after delay
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}