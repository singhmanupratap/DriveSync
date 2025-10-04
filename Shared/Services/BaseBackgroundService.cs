using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Services;

/// <summary>
/// Base background service that provides mandatory database synchronization patterns.
/// All DriveSync background services should inherit from this class to ensure
/// proper database copy operations on start, completion, and shutdown.
/// </summary>
public abstract class BaseBackgroundService : BackgroundService
{
    protected readonly ILogger<BaseBackgroundService> _logger;
    protected readonly IServiceProvider _serviceProvider;
    private readonly string _serviceName;

    protected BaseBackgroundService(
        ILogger logger,
        IServiceProvider serviceProvider,
        string serviceName)
    {
        _logger = (ILogger<BaseBackgroundService>)logger;
        _serviceProvider = serviceProvider;
        _serviceName = serviceName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // MANDATORY: Copy database from remote to local on start
            await PerformDatabaseCopyToLocalAsync();
            
            _logger.LogInformation("{ServiceName} started successfully with database sync", _serviceName);

            // Call the derived service's main execution logic
            await ExecuteServiceAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in {ServiceName}", _serviceName);
            throw;
        }
        finally
        {
            // MANDATORY: Copy database back to remote on shutdown
            await PerformFinalDatabaseSyncAsync();
            _logger.LogInformation("{ServiceName} stopped", _serviceName);
        }
    }

    /// <summary>
    /// Override this method to implement the main service logic.
    /// Database sync is handled automatically by the base class.
    /// </summary>
    protected abstract Task ExecuteServiceAsync(CancellationToken stoppingToken);

    /// <summary>
    /// Executes a service operation with automatic database sync and disaster recovery.
    /// Use this method to wrap your main service operations.
    /// </summary>
    protected async Task ExecuteWithDatabaseSyncAsync(Func<Task> serviceOperation, string operationName = "operation")
    {
        try
        {
            await serviceOperation();
            
            // MANDATORY: Copy database back to remote after successful operation
            await PerformDatabaseSyncToRemoteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during {OperationName} in {ServiceName}", operationName, _serviceName);
            
            // DISASTER RECOVERY: Ensure database copy-back even on errors
            await PerformDisasterRecoveryDatabaseSyncAsync();
            throw; // Re-throw to allow caller to handle
        }
    }

    /// <summary>
    /// Performs the initial database copy from remote to local.
    /// </summary>
    private async Task PerformDatabaseCopyToLocalAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var databaseCopyService = scope.ServiceProvider.GetService<IDatabaseCopyService>();
            
            if (databaseCopyService != null)
            {
                await databaseCopyService.CopyDatabaseToLocalAsync();
                _logger.LogInformation("Initial database copy completed for {ServiceName}", _serviceName);
            }
            else
            {
                _logger.LogWarning("DatabaseCopyService not found for {ServiceName} - continuing without database sync", _serviceName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy database to local for {ServiceName}", _serviceName);
            // Don't throw - service should continue even if initial copy fails
        }
    }

    /// <summary>
    /// Performs database sync back to remote after successful operations.
    /// </summary>
    private async Task PerformDatabaseSyncToRemoteAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var databaseCopyService = scope.ServiceProvider.GetService<IDatabaseCopyService>();
            
            if (databaseCopyService != null)
            {
                await databaseCopyService.CopyDatabaseToRemoteAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync database to remote for {ServiceName}", _serviceName);
            // Don't throw - this shouldn't break the main operation
        }
    }

    /// <summary>
    /// Performs disaster recovery database sync after exceptions.
    /// </summary>
    private async Task PerformDisasterRecoveryDatabaseSyncAsync()
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var databaseCopyService = scope.ServiceProvider.GetService<IDatabaseCopyService>();
            
            if (databaseCopyService != null)
            {
                await databaseCopyService.CopyDatabaseToRemoteAsync();
                _logger.LogInformation("Disaster recovery database sync completed for {ServiceName}", _serviceName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed disaster recovery database sync for {ServiceName}", _serviceName);
            // Don't throw - we're already in error recovery
        }
    }

    /// <summary>
    /// Performs final database sync during shutdown.
    /// </summary>
    private async Task PerformFinalDatabaseSyncAsync()
    {
        try
        {
            // Check if service provider is still available
            if (_serviceProvider == null)
            {
                _logger.LogWarning("Service provider unavailable during final database sync for {ServiceName}", _serviceName);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var databaseCopyService = scope.ServiceProvider.GetService<IDatabaseCopyService>();
            
            if (databaseCopyService != null)
            {
                await databaseCopyService.CopyDatabaseToRemoteAsync();
                _logger.LogInformation("Final database sync completed for {ServiceName}", _serviceName);
            }
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("Service provider disposed during final database sync for {ServiceName}", _serviceName);
            // Don't throw - we're in shutdown and the service provider is already disposed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed final database sync for {ServiceName}", _serviceName);
            // Don't throw - we're in shutdown
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("{ServiceName} stopping - performing final database sync", _serviceName);
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var databaseCopyService = scope.ServiceProvider.GetService<IDatabaseCopyService>();
            
            if (databaseCopyService != null)
            {
                // MANDATORY: Final database copy with timeout
                var syncTask = databaseCopyService.CopyDatabaseToRemoteAsync();
                var completedTask = await Task.WhenAny(syncTask, Task.Delay(TimeSpan.FromSeconds(10), cancellationToken));
                
                if (completedTask == syncTask)
                {
                    _logger.LogInformation("Final database sync completed successfully for {ServiceName}", _serviceName);
                }
                else
                {
                    _logger.LogWarning("Final database sync timed out for {ServiceName}", _serviceName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during final database sync for {ServiceName}", _serviceName);
        }
        
        await base.StopAsync(cancellationToken);
    }
}