using DriveSync.WebUI.Services;

namespace DriveSync.WebUI.Services
{
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
                        
                    _logger.LogDebug("Performing periodic database sync to remote");
                    
                    using var scope = _serviceProvider.CreateScope();
                    var databaseCopyService = scope.ServiceProvider.GetRequiredService<DatabaseCopyService>();
                    
                    await databaseCopyService.SyncAllChangesToRemoteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during periodic database sync");
                }
            }
            
            _logger.LogInformation("Database sync background service stopped");
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Performing final database sync before shutdown");
            
            try
            {
                // Create a timeout for the sync operation (20 seconds max)
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);
                
                using var scope = _serviceProvider.CreateScope();
                var databaseCopyService = scope.ServiceProvider.GetRequiredService<DatabaseCopyService>();
                
                var syncTask = databaseCopyService.SyncAllChangesToRemoteAsync();
                await syncTask.WaitAsync(combinedCts.Token);
                
                _logger.LogInformation("Final database sync completed successfully");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Final database sync was cancelled due to application shutdown timeout");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Final database sync timed out after 20 seconds");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during final database sync");
            }
            
            await base.StopAsync(stoppingToken);
        }
    }
}