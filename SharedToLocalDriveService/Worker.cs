using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedToLocalDriveService.Configuration;
using SharedToLocalDriveService.Services;

namespace SharedToLocalDriveService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IFileSyncService _fileSyncService;
    private readonly ServiceConfiguration _config;

    public Worker(ILogger<Worker> logger, IFileSyncService fileSyncService, IOptions<ServiceConfiguration> config)
    {
        _logger = logger;
        _fileSyncService = fileSyncService;
        _config = config.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SharedToLocalDriveService started at: {time}", DateTimeOffset.Now);
        _logger.LogInformation("Sync interval: {interval} minutes", _config.SyncIntervalMinutes);
        _logger.LogInformation("Shared folder: {sharedPath}", _config.SharedFolderPath);
        _logger.LogInformation("Local folder: {localPath}", _config.LocalFolderPath);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Starting sync cycle at: {time}", DateTimeOffset.Now);
                await _fileSyncService.SyncFilesAsync(stoppingToken);
                _logger.LogInformation("Sync cycle completed at: {time}", DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during sync cycle");
            }

            // Wait for the configured interval
            var delay = TimeSpan.FromMinutes(_config.SyncIntervalMinutes);
            _logger.LogDebug("Waiting {delay} minutes before next sync cycle", _config.SyncIntervalMinutes);
            
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Service is stopping...");
                break;
            }
        }

        _logger.LogInformation("SharedToLocalDriveService stopped at: {time}", DateTimeOffset.Now);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SharedToLocalDriveService is starting...");
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("SharedToLocalDriveService is stopping...");
        await base.StopAsync(cancellationToken);
    }
}