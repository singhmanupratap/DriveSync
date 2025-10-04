using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Services;

namespace WebUI.BackgroundServices;

public class DatabaseSyncBackgroundService : BackgroundService
{
    private readonly ILogger<DatabaseSyncBackgroundService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _syncInterval;

    public DatabaseSyncBackgroundService(
        ILogger<DatabaseSyncBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        
        // Get sync interval from configuration, default to 5 minutes
        var intervalMinutes = configuration.GetValue<int>("DatabaseSync:IntervalMinutes", 5);
        _syncInterval = TimeSpan.FromMinutes(intervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Database sync background service started with interval: {Interval}", _syncInterval);
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_syncInterval, stoppingToken);
                
                if (stoppingToken.IsCancellationRequested)
                    break;
                    
                using var scope = _serviceProvider.CreateScope();
                var databaseCopyService = scope.ServiceProvider.GetService<IDatabaseCopyService>();
                
                if (databaseCopyService != null)
                {
                    await databaseCopyService.SyncAllChangesToRemoteAsync();
                }
                else
                {
                    _logger.LogWarning("DatabaseCopyService not found in service provider");
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during periodic database sync");
            }
        }
        
        _logger.LogInformation("Database sync background service stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Database sync background service stopping - performing final sync");
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var databaseCopyService = scope.ServiceProvider.GetService<IDatabaseCopyService>();
            
            if (databaseCopyService != null)
            {
                var syncTask = databaseCopyService.SyncAllChangesToRemoteAsync();
                var completedTask = await Task.WhenAny(syncTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
                
                if (completedTask == syncTask)
                {
                    _logger.LogInformation("Final database sync completed successfully");
                }
                else
                {
                    _logger.LogWarning("Final database sync timed out");
                }
            }
            else
            {
                _logger.LogWarning("DatabaseCopyService not found in service provider for final sync");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during final database sync");
        }
        
        await base.StopAsync(cancellationToken);
    }
}