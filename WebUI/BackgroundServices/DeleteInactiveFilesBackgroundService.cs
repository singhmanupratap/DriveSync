using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Services;

namespace DriveSync.WebUI.BackgroundServices;

public class DeleteInactiveFilesBackgroundService : BaseBackgroundService
{
    private readonly ILogger<DeleteInactiveFilesBackgroundService> _deleteLogger;
    private readonly IConfiguration _configuration;
    private readonly IFileDeleteService _fileDeleteService;
    private readonly int _deleteIntervalMinutes;
    private readonly bool _enabled;

    public DeleteInactiveFilesBackgroundService(
        ILogger<DeleteInactiveFilesBackgroundService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IFileDeleteService fileDeleteService)
        : base(logger, serviceProvider, "DeleteInactive")
    {
        _deleteLogger = logger;
        _configuration = configuration;
        _fileDeleteService = fileDeleteService;
        _deleteIntervalMinutes = _configuration.GetValue<int>("DeleteInactiveFiles:IntervalMinutes", 15);
        _enabled = _configuration.GetValue<bool>("DeleteInactiveFiles:Enabled", true);
    }

    protected override async Task ExecuteServiceAsync(CancellationToken stoppingToken)
    {
        if (!_enabled)
        {
            _deleteLogger.LogInformation("DeleteInactiveFilesBackgroundService is disabled in configuration");
            return;
        }

        await _fileDeleteService.InitializeAsync();
        _deleteLogger.LogInformation("DeleteInactiveFilesBackgroundService started successfully - Delete interval: {DeleteInterval} minutes", _deleteIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Use the base class method for automatic database sync
            await ExecuteWithDatabaseSyncAsync(async () =>
            {
                await _fileDeleteService.ProcessInactiveFilesAsync(stoppingToken);
            }, "inactive file processing");

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_deleteIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}