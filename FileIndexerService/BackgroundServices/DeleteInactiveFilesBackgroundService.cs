using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Services;

namespace FileIndexerService.BackgroundServices;

public class DeleteInactiveFilesBackgroundService : BackgroundService
{
    private readonly ILogger<DeleteInactiveFilesBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IFileDeleteService _fileDeleteService;
    private readonly IDatabaseCopyService _databaseCopyService;
    private readonly int _deleteIntervalMinutes;
    private readonly bool _enabled;

    public DeleteInactiveFilesBackgroundService(
        ILogger<DeleteInactiveFilesBackgroundService> logger,
        IConfiguration configuration,
        IFileDeleteService fileDeleteService,
        IDatabaseCopyService databaseCopyService)
    {
        _logger = logger;
        _configuration = configuration;
        _fileDeleteService = fileDeleteService;
        _databaseCopyService = databaseCopyService;
        _deleteIntervalMinutes = _configuration.GetValue<int>("DeleteInactiveFiles:IntervalMinutes", 15);
        _enabled = _configuration.GetValue<bool>("DeleteInactiveFiles:Enabled", true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            if (!_enabled)
            {
                _logger.LogInformation("DeleteInactiveFilesBackgroundService is disabled in configuration");
                return;
            }

            await _fileDeleteService.InitializeAsync();
            _logger.LogInformation("DeleteInactiveFilesBackgroundService started successfully - Delete interval: {DeleteInterval} minutes", _deleteIntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _fileDeleteService.ProcessInactiveFilesAsync(stoppingToken);
                    await _databaseCopyService.CopyDatabaseToRemoteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during inactive file processing cycle");
                }

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in DeleteInactiveFilesBackgroundService");
            throw;
        }
        finally
        {
            _logger.LogInformation("DeleteInactiveFilesBackgroundService stopped");
        }
    }
}