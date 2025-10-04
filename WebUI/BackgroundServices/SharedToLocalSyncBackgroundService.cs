using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Services;
using Shared.Configuration;

namespace DriveSync.WebUI.BackgroundServices;

public class SharedToLocalSyncBackgroundService : BaseBackgroundService
{
    private readonly ILogger<SharedToLocalSyncBackgroundService> _syncLogger;
    private readonly IConfiguration _configuration;
    private readonly IFileSyncService _fileSyncService;
    private readonly int _syncIntervalMinutes;

    public SharedToLocalSyncBackgroundService(
        ILogger<SharedToLocalSyncBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IFileSyncService fileSyncService)
        : base(logger, serviceProvider, "SharedToLocal")
    {
        _syncLogger = logger;
        _configuration = configuration;
        _fileSyncService = fileSyncService;
        
        var config = new SharedToLocalConfiguration();
        _configuration.GetSection(SharedToLocalConfiguration.SectionName).Bind(config);
        _syncIntervalMinutes = config.SyncIntervalMinutes;
    }

    protected override async Task ExecuteServiceAsync(CancellationToken stoppingToken)
    {
        await _fileSyncService.InitializeAsync();
        _syncLogger.LogInformation("SharedToLocalSyncBackgroundService started successfully - Sync interval: {SyncInterval} minutes", _syncIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Use the base class method for automatic database sync
            await ExecuteWithDatabaseSyncAsync(async () =>
            {
                _syncLogger.LogInformation("Starting sync cycle at: {time}", DateTimeOffset.Now);
                await _fileSyncService.SyncFilesAsync(stoppingToken);
                _syncLogger.LogInformation("Sync cycle completed at: {time}", DateTimeOffset.Now);
            }, "file sync cycle");

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
}