using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Services;
using Shared.Configuration;

namespace SharedToLocalDriveService.BackgroundServices;

public class SharedToLocalDriveBackgroundService : BackgroundService
{
    private readonly ILogger<SharedToLocalDriveBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IFileSyncService _fileSyncService;
    private readonly int _syncIntervalMinutes;

    public SharedToLocalDriveBackgroundService(
        ILogger<SharedToLocalDriveBackgroundService> logger,
        IConfiguration configuration,
        IFileSyncService fileSyncService)
    {
        _logger = logger;
        _configuration = configuration;
        _fileSyncService = fileSyncService;
        
        var config = new SharedToLocalConfiguration();
        _configuration.GetSection(SharedToLocalConfiguration.SectionName).Bind(config);
        _syncIntervalMinutes = config.SyncIntervalMinutes;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _fileSyncService.InitializeAsync();
            _logger.LogInformation("SharedToLocalDriveBackgroundService started successfully - Sync interval: {SyncInterval} minutes", _syncIntervalMinutes);

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

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_syncIntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in SharedToLocalDriveBackgroundService");
            throw;
        }
        finally
        {
            _logger.LogInformation("SharedToLocalDriveBackgroundService stopped");
        }
    }
}