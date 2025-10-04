using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Services;

namespace DriveSync.WebUI.BackgroundServices;

public class DatabaseSyncBackgroundService : BaseBackgroundService
{
    private readonly ILogger<DatabaseSyncBackgroundService> _databaseSyncLogger;
    private readonly TimeSpan _syncInterval;

    public DatabaseSyncBackgroundService(
        ILogger<DatabaseSyncBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
        : base(logger, serviceProvider, "DatabaseSync")
    {
        _databaseSyncLogger = logger;
        
        // Get sync interval from configuration, default to 5 minutes
        var intervalMinutes = configuration.GetValue<int>("DatabaseSync:IntervalMinutes", 5);
        _syncInterval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteServiceAsync(CancellationToken stoppingToken)
    {
        _databaseSyncLogger.LogInformation("Database sync background service started with interval: {Interval}", _syncInterval);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_syncInterval, stoppingToken);
                
                if (stoppingToken.IsCancellationRequested)
                    break;
                
                // Use the base class method for automatic database sync
                await ExecuteWithDatabaseSyncAsync(async () =>
                {
                    // The actual sync work is done by the base class
                    // This is just a periodic trigger
                    _databaseSyncLogger.LogInformation("Periodic database sync triggered");
                }, "periodic database sync");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _databaseSyncLogger.LogError(ex, "Error during periodic database sync");
            }
        }
    }
}