using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Data;
using Shared.Models;

namespace Shared.Services;

public interface IFileDeleteService
{
    Task ProcessInactiveFilesAsync(CancellationToken cancellationToken = default);
    Task InitializeAsync();
    void Initialize(string inputFolderPath, string localDatabasePath, int batchSize = 100);
    void SetLastProcessedDate(DateTime? lastProcessedDate);
}

public class FileDeleteService : IFileDeleteService
{
    private readonly ILogger<FileDeleteService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDatabaseCopyService _databaseCopyService;
    private FileIndexerDatabase? _database;
    
    // Configuration properties
    private string _inputFolderPath = string.Empty;
    private string _localDatabasePath = string.Empty;
    private int _batchSize = 100;
    
    // State tracking
    private DateTime? _lastProcessedDate = null;

    public FileDeleteService(
        ILogger<FileDeleteService> logger, 
        IConfiguration configuration,
        IDatabaseCopyService databaseCopyService)
    {
        _logger = logger;
        _configuration = configuration;
        _databaseCopyService = databaseCopyService;
    }

    public void Initialize(string inputFolderPath, string localDatabasePath, int batchSize = 100)
    {
        _inputFolderPath = inputFolderPath;
        _localDatabasePath = localDatabasePath;
        _batchSize = batchSize;
        
        var connectionString = $"Data Source={_localDatabasePath}";
        _database = new FileIndexerDatabase(connectionString);
    }

    public async Task InitializeAsync()
    {
        await _databaseCopyService.CopyDatabaseToLocalAsync();
        
        var dbConfig = _configuration.GetSection("DatabaseConfig");
        var inputFolderPath = _configuration["InputFolder"] ?? throw new InvalidOperationException("InputFolder not configured");
        var localDatabasePath = dbConfig["LocalDatabasePath"] ?? "fileindexer_local.db";
        var batchSize = _configuration.GetValue<int>("DeleteInactiveFiles:BatchSize", 100);
        
        Initialize(inputFolderPath, localDatabasePath, batchSize);
    }

    public void SetLastProcessedDate(DateTime? lastProcessedDate)
    {
        _lastProcessedDate = lastProcessedDate;
    }

    public async Task ProcessInactiveFilesAsync(CancellationToken cancellationToken = default)
    {
        if (_database == null || string.IsNullOrEmpty(_inputFolderPath))
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize first.");
        }

        _logger.LogInformation("Starting inactive file processing...");

        try
        {
            // Get inactive files that need processing
            var inactiveFiles = await _database.GetInactiveFilesForDeletionAsync(_lastProcessedDate, _batchSize);
            var processedCount = 0;
            var batchCount = 0;

            _logger.LogInformation("Found {InactiveFileCount} inactive files to process", inactiveFiles.Count);

            foreach (var record in inactiveFiles)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Inactive file processing cancelled");
                    break;
                }

                try
                {
                    // These are already marked as inactive, we can process them for deletion
                    // or perform other cleanup operations
                    
                    _logger.LogDebug("Processing inactive file: {RelativePath}", record.RelativePath);
                    
                    // Here you could add logic to:
                    // 1. Actually delete the database record
                    // 2. Move files to a cleanup folder
                    // 3. Archive old records
                    // For now, we'll just log them
                    
                    processedCount++;
                    batchCount++;

                    // Process in batches for better performance
                    if (batchCount >= _batchSize)
                    {
                        _logger.LogInformation("Processed batch of {BatchSize} files. Total processed: {ProcessedCount}", 
                            _batchSize, processedCount);
                        batchCount = 0;
                        
                        // Small delay to prevent overwhelming the system
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing inactive file record: {RelativePath}", record.RelativePath);
                }
            }

            _logger.LogInformation("Inactive file processing completed. Processed: {ProcessedCount} inactive files", 
                processedCount);

            // Update last processed timestamp
            _lastProcessedDate = DateTime.Now;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during inactive file processing");
            throw;
        }
    }
}