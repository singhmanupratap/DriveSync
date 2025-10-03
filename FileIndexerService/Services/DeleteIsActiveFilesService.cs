using DriveSync.Shared.Data;
using DriveSync.Shared.Models;

namespace FileIndexerService.Services;

public class DeleteIsActiveFilesService : BackgroundService
{
    private readonly ILogger<DeleteIsActiveFilesService> _logger;
    private readonly IConfiguration _configuration;
    private FileIndexerDatabase? _database;
    
    // Configuration properties
    private string _inputFolderPath = string.Empty;
    private string _databasePath = string.Empty;
    private string _localDatabasePath = string.Empty;
    private int _deleteIntervalMinutes = 15;
    private int _batchSize = 100;
    private bool _enabled = true;
    
    // State tracking
    private DateTime? _lastProcessedDate = null;

    public DeleteIsActiveFilesService(ILogger<DeleteIsActiveFilesService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            LoadConfiguration();
            
            if (!_enabled)
            {
                _logger.LogInformation("DeleteIsActiveFilesService is disabled in configuration");
                return;
            }
            
            // Initial database setup
            await CopyDatabaseToLocalAsync();
            InitializeDatabase();
            
            _logger.LogInformation("DeleteIsActiveFilesService started successfully - Monitoring folder: {InputFolder}", _inputFolderPath);
            _logger.LogInformation("Delete service will run every {DeleteInterval} minutes with batch size of {BatchSize}", _deleteIntervalMinutes, _batchSize);

            // Initialize last processed date from database
            _lastProcessedDate = await _database!.GetLastProcessedModificationDateAsync();
            if (_lastProcessedDate.HasValue)
            {
                _logger.LogInformation("Starting from last processed modification date: {LastProcessedDate}", _lastProcessedDate.Value);
            }
            else
            {
                _logger.LogInformation("No previous processing date found, will process all inactive files");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessInactiveFilesAsync(stoppingToken);
                    await CopyDatabaseToRemoteAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during inactive file deletion cycle");
                }

                // Wait for the configured interval before next deletion cycle
                _logger.LogDebug("Waiting {DeleteInterval} minutes before next deletion cycle...", _deleteIntervalMinutes);
                await Task.Delay(TimeSpan.FromMinutes(_deleteIntervalMinutes), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical error in DeleteIsActiveFilesService");
            throw;
        }
        finally
        {
            _database?.Dispose();
            
            // Final copy of database to remote location
            try
            {
                await CopyDatabaseToRemoteAsync();
                _logger.LogInformation("Final database copy completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during final database copy");
            }
        }
    }

    private void LoadConfiguration()
    {
        var config = _configuration.GetSection("FileIndexerConfiguration");
        _inputFolderPath = config["InputFolderPath"] ?? throw new InvalidOperationException("InputFolderPath is required");
        _databasePath = config["DatabasePath"] ?? throw new InvalidOperationException("DatabasePath is required");
        _localDatabasePath = config["LocalDatabasePath"] ?? Path.Combine(AppContext.BaseDirectory, "fileindexer_local.db");

        var deleteConfig = _configuration.GetSection("DeleteServiceConfiguration");
        _deleteIntervalMinutes = deleteConfig.GetValue<int>("DeleteIntervalMinutes", 15);
        _batchSize = deleteConfig.GetValue<int>("BatchSize", 100);
        _enabled = deleteConfig.GetValue<bool>("Enabled", true);

        _logger.LogInformation("Delete service configuration loaded - Input: {Input}, Database: {Database}, LocalDatabase: {LocalDatabase}", 
            _inputFolderPath, _databasePath, _localDatabasePath);
        _logger.LogInformation("Delete service settings - Interval: {Interval}min, BatchSize: {BatchSize}, Enabled: {Enabled}", 
            _deleteIntervalMinutes, _batchSize, _enabled);
    }

    private void InitializeDatabase()
    {
        var connectionString = $"Data Source={_localDatabasePath}";
        _database = new FileIndexerDatabase(connectionString);
        _logger.LogInformation("Delete service database initialized at local path: {LocalDatabasePath}", _localDatabasePath);
    }

    private async Task CopyDatabaseToLocalAsync()
    {
        try
        {
            if (!File.Exists(_localDatabasePath) && File.Exists(_databasePath))
            {
                await Task.Run(() => File.Copy(_databasePath, _localDatabasePath, overwrite: false));
                _logger.LogInformation("Database copied from {RemotePath} to {LocalPath}", _databasePath, _localDatabasePath);
            }
            else if (File.Exists(_localDatabasePath))
            {
                _logger.LogInformation("Local database already exists at {LocalPath}, using existing copy", _localDatabasePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying database to local path");
            throw;
        }
    }

    private async Task CopyDatabaseToRemoteAsync()
    {
        try
        {
            if (File.Exists(_localDatabasePath))
            {
                var directory = Path.GetDirectoryName(_databasePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await Task.Run(() => File.Copy(_localDatabasePath, _databasePath, overwrite: true));
                _logger.LogDebug("Database copied from {LocalPath} to {RemotePath}", _localDatabasePath, _databasePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying database to remote path");
        }
    }

    private async Task ProcessInactiveFilesAsync(CancellationToken stoppingToken)
    {
        if (_database == null)
        {
            _logger.LogWarning("Database not initialized, skipping deletion cycle");
            return;
        }

        _logger.LogInformation("Starting inactive file deletion cycle - Last processed date: {LastProcessedDate}", _lastProcessedDate);

        var inactiveFiles = await _database.GetInactiveFilesForDeletionAsync(_lastProcessedDate, _batchSize);
        
        if (!inactiveFiles.Any())
        {
            _logger.LogInformation("No inactive files found for deletion");
            return;
        }

        _logger.LogInformation("Found {Count} inactive files to process", inactiveFiles.Count);

        int deletedCount = 0;
        int failedCount = 0;
        int restoredCount = 0;
        int ignoredCount = 0;
        long totalSizeDeleted = 0;
        DateTime latestModificationDate = _lastProcessedDate ?? DateTime.MinValue;

        foreach (var file in inactiveFiles)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Cancellation requested, stopping file deletion");
                break;
            }

            try
            {
                var fullPath = Path.Combine(_inputFolderPath, file.GetFullRelativePath());
                
                // Safety check: ensure the file is within the configured input folder
                var normalizedInputPath = Path.GetFullPath(_inputFolderPath);
                var normalizedFilePath = Path.GetFullPath(fullPath);
                
                if (!normalizedFilePath.StartsWith(normalizedInputPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping file outside of input folder: {FilePath}", fullPath);
                    continue;
                }
                
                if (File.Exists(fullPath))
                {
                    var fileInfo = new FileInfo(fullPath);
                    
                    // Additional safety check: verify file is not read-only or system file
                    if ((fileInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        _logger.LogWarning("Skipping read-only file: {FilePath}", fullPath);
                        continue;
                    }
                    
                    if ((fileInfo.Attributes & FileAttributes.System) == FileAttributes.System)
                    {
                        _logger.LogWarning("Skipping system file: {FilePath}", fullPath);
                        continue;
                    }
                    
                    // Check if file modification date is newer than database record
                    if (fileInfo.LastWriteTime > file.ModificationDate)
                    {
                        // File has been modified/restored - create new active record
                        var restored = await _database.InsertRestoredFileRecordAsync(file, fileInfo.LastWriteTime, fileInfo.Length);
                        if (restored)
                        {
                            _logger.LogInformation("File restored with newer modification date - created new active record: {FilePath} (DB: {DbDate}, File: {FileDate})", 
                                fullPath, file.ModificationDate, fileInfo.LastWriteTime);
                            restoredCount++;
                        }
                        else
                        {
                            _logger.LogWarning("Failed to create restored file record: {FilePath}", fullPath);
                            failedCount++;
                        }
                        
                        // Remove the old inactive record
                        await _database.DeleteFileRecordAsync(file.Id);
                    }
                    else
                    {
                        // File exists but hasn't been modified - delete it
                        File.Delete(fullPath);
                        totalSizeDeleted += file.FileSizeBytes;
                        _logger.LogDebug("Deleted file: {FilePath} (Size: {Size} bytes)", fullPath, file.FileSizeBytes);
                        
                        // Remove the record from database
                        await _database.DeleteFileRecordAsync(file.Id);
                        deletedCount++;
                    }
                }
                else
                {
                    // File not available - ignore (don't delete from database)
                    _logger.LogDebug("File not found on disk, ignoring: {FilePath}", fullPath);
                    ignoredCount++;
                }

                // Track the latest modification date for progress tracking
                if (file.ModificationDate > latestModificationDate)
                {
                    latestModificationDate = file.ModificationDate;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file: {FilePath}", Path.Combine(_inputFolderPath, file.GetFullRelativePath()));
                failedCount++;
            }
        }

        // Update the last processed date
        if (latestModificationDate > (_lastProcessedDate ?? DateTime.MinValue))
        {
            _lastProcessedDate = latestModificationDate;
        }

        _logger.LogInformation("Deletion cycle completed - Deleted: {DeletedCount}, Restored: {RestoredCount}, Ignored: {IgnoredCount}, Failed: {FailedCount}, Total size freed: {SizeFreed:N0} bytes ({SizeFreedMB:N2} MB)", 
            deletedCount, restoredCount, ignoredCount, failedCount, totalSizeDeleted, totalSizeDeleted / 1024.0 / 1024.0);

        if (_lastProcessedDate.HasValue)
        {
            _logger.LogInformation("Last processed modification date updated to: {LastProcessedDate}", _lastProcessedDate.Value);
        }

        // Clean up empty directories
        if (deletedCount > 0)
        {
            await CleanupEmptyDirectoriesAsync();
        }
    }

    private async Task CleanupEmptyDirectoriesAsync()
    {
        try
        {
            _logger.LogDebug("Starting empty directory cleanup");
            await Task.Run(() =>
            {
                CleanupEmptyDirectoriesRecursive(_inputFolderPath);
            });
            _logger.LogDebug("Empty directory cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during empty directory cleanup");
        }
    }

    private void CleanupEmptyDirectoriesRecursive(string directoryPath)
    {
        try
        {
            // Don't delete the root input folder
            if (string.Equals(directoryPath, _inputFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!Directory.Exists(directoryPath))
            {
                return;
            }

            // First, recursively clean subdirectories
            var subdirectories = Directory.GetDirectories(directoryPath);
            foreach (var subdirectory in subdirectories)
            {
                CleanupEmptyDirectoriesRecursive(subdirectory);
            }

            // Then check if this directory is now empty and can be deleted
            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                Directory.Delete(directoryPath);
                _logger.LogDebug("Deleted empty directory: {DirectoryPath}", directoryPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete directory: {DirectoryPath}", directoryPath);
        }
    }
}