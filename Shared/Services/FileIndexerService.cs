using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Data;
using Shared.Models;

namespace Shared.Services;

public interface IFileIndexerService
{
    Task IndexFilesAsync(CancellationToken cancellationToken = default);
    Task LogStatisticsAsync();
    Task InitializeAsync();
    void Initialize(string inputFolderPath, string localDatabasePath);
}

public class FileIndexerService : IFileIndexerService
{
    private readonly ILogger<FileIndexerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDatabaseCopyService _databaseCopyService;
    private FileIndexerDatabase? _database;
    
    // Configuration properties
    private string _inputFolderPath = string.Empty;
    private string _localDatabasePath = string.Empty;

    public FileIndexerService(
        ILogger<FileIndexerService> logger, 
        IConfiguration configuration,
        IDatabaseCopyService databaseCopyService)
    {
        _logger = logger;
        _configuration = configuration;
        _databaseCopyService = databaseCopyService;
    }

    public void Initialize(string inputFolderPath, string localDatabasePath)
    {
        _inputFolderPath = inputFolderPath;
        _localDatabasePath = localDatabasePath;
        
        var connectionString = $"Data Source={_localDatabasePath}";
        _database = new FileIndexerDatabase(connectionString);
    }

    public async Task InitializeAsync()
    {
        await _databaseCopyService.CopyDatabaseToLocalAsync();
        
        var dbConfig = _configuration.GetSection("DatabaseConfig");
        var inputFolderPath = _configuration["InputFolder"] ?? throw new InvalidOperationException("InputFolder not configured");
        var localDatabasePath = dbConfig["LocalDatabasePath"] ?? "fileindexer_local.db";
        
        Initialize(inputFolderPath, localDatabasePath);
    }

    public async Task IndexFilesAsync(CancellationToken cancellationToken = default)
    {
        if (_database == null || string.IsNullOrEmpty(_inputFolderPath))
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize first.");
        }

        if (!Directory.Exists(_inputFolderPath))
        {
            _logger.LogWarning("Input folder does not exist: {InputFolder}", _inputFolderPath);
            return;
        }

        _logger.LogInformation("Starting file indexing for folder: {InputFolder}", _inputFolderPath);

        var files = Directory.GetFiles(_inputFolderPath, "*", SearchOption.AllDirectories);
        var processedCount = 0;
        var newFilesCount = 0;
        var updatedFilesCount = 0;

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("File indexing cancelled");
                break;
            }

            try
            {
                var fileInfo = new FileInfo(file);
                var relativePath = Path.GetRelativePath(_inputFolderPath, file);
                
                var existingRecord = await _database.GetFileRecordAsync(relativePath, fileInfo.Name);
                
                if (existingRecord == null)
                {
                    // New file
                    var newRecord = new FileRecord
                    {
                        FileName = fileInfo.Name,
                        RelativePath = relativePath,
                        FileSizeBytes = fileInfo.Length,
                        CreationDate = fileInfo.CreationTime,
                        ModificationDate = fileInfo.LastWriteTime,
                        IndexedDate = DateTime.Now,
                        IsActive = true
                    };

                    await _database.InsertFileRecordAsync(newRecord);
                    newFilesCount++;
                    _logger.LogDebug("Indexed new file: {RelativePath}", relativePath);
                }
                else if (existingRecord.ModificationDate != fileInfo.LastWriteTime || 
                         existingRecord.FileSizeBytes != fileInfo.Length)
                {
                    // Updated file
                    existingRecord.FileSizeBytes = fileInfo.Length;
                    existingRecord.ModificationDate = fileInfo.LastWriteTime;
                    existingRecord.IndexedDate = DateTime.Now;
                    existingRecord.IsActive = true;

                    await _database.UpdateFileRecordAsync(existingRecord.Id, existingRecord);
                    updatedFilesCount++;
                    _logger.LogDebug("Updated file: {RelativePath}", relativePath);
                }
                else if (!existingRecord.IsActive)
                {
                    // File exists again, mark as active
                    existingRecord.IsActive = true;
                    existingRecord.IndexedDate = DateTime.Now;
                    await _database.UpdateFileRecordAsync(existingRecord.Id, existingRecord);
                    updatedFilesCount++;
                    _logger.LogDebug("Reactivated file: {RelativePath}", relativePath);
                }

                processedCount++;

                // Log progress every 1000 files
                if (processedCount % 1000 == 0)
                {
                    _logger.LogInformation("Processed {ProcessedCount} files so far...", processedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing file: {FilePath}", file);
            }
        }

        _logger.LogInformation("File indexing completed. Processed: {ProcessedCount}, New: {NewCount}, Updated: {UpdatedCount}", 
            processedCount, newFilesCount, updatedFilesCount);
    }

    public async Task LogStatisticsAsync()
    {
        if (_database == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize first.");
        }

        try
        {
            var statistics = await _database.GetStatisticsAsync();
            var totalFiles = statistics.total;
            var activeFiles = statistics.active;
            var inactiveFiles = statistics.inactive;

            _logger.LogInformation("Database Statistics - Total: {Total}, Active: {Active}, Inactive: {Inactive}", 
                totalFiles, activeFiles, inactiveFiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving database statistics");
        }
    }
}