using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Services;

namespace FileIndexerService.BackgroundServices;

public class FileIndexerBackgroundService : BackgroundService
{
    private readonly ILogger<FileIndexerBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IFileIndexerService _fileIndexerService;
    private readonly IDatabaseCopyService _databaseCopyService;
    private readonly int _scanIntervalMinutes;

    public FileIndexerBackgroundService(
        ILogger<FileIndexerBackgroundService> logger,
        IConfiguration configuration,
        IFileIndexerService fileIndexerService,
        IDatabaseCopyService databaseCopyService)
    {
        _logger = logger;
        _configuration = configuration;
        _fileIndexerService = fileIndexerService;
        _databaseCopyService = databaseCopyService;
        _scanIntervalMinutes = _configuration.GetValue<int>("ScanIntervalMinutes", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _fileIndexerService.InitializeAsync();
            _logger.LogInformation("FileIndexerBackgroundService started successfully - Scan interval: {ScanInterval} minutes", _scanIntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _fileIndexerService.IndexFilesAsync(stoppingToken);
                    await _fileIndexerService.LogStatisticsAsync();
                    await _databaseCopyService.CopyDatabaseToRemoteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during file indexing cycle");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_scanIntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in FileIndexerBackgroundService");
            throw;
        }
        finally
        {
            _logger.LogInformation("FileIndexerBackgroundService stopped");
        }
    }
}