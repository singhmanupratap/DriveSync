using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Configuration;
using Shared.Data;
using Shared.Models;

namespace Shared.Services;

public interface IFileIndexerService
{
    Task IndexFilesAsync(CancellationToken cancellationToken = default);
    Task IndexFilesAsync(ScanType scanType, CancellationToken cancellationToken = default);
    Task<ScanType> DetermineScanTypeAsync();
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
    private readonly string _hostName;
    private readonly string _timeZone;
    private FileIndexerConfiguration _config;

    public FileIndexerService(
        ILogger<FileIndexerService> logger, 
        IConfiguration configuration,
        IDatabaseCopyService databaseCopyService)
    {
        _logger = logger;
        _configuration = configuration;
        _databaseCopyService = databaseCopyService;
        
        // Initialize configuration
        _config = new FileIndexerConfiguration();
        _configuration.GetSection(FileIndexerConfiguration.SectionName).Bind(_config);
        
        // Get host name
        _hostName = Environment.MachineName;
        
        // Get timezone - first check host-specific config, then global config, then system default
        _timeZone = GetHostSpecificConfig().TimeZone ?? _config.TimeZone ?? TimeZoneInfo.Local.Id;
        
        _logger.LogInformation("FileIndexerService initialized for host: {HostName}, TimeZone: {TimeZone}", _hostName, _timeZone);
    }

    private HostSpecificConfig GetHostSpecificConfig()
    {
        if (_config.HostConfigs.TryGetValue(_hostName, out var hostConfig))
        {
            return hostConfig;
        }
        
        // Return default configuration
        return new HostSpecificConfig
        {
            ScanMode = _config.ScanMode,
            ScanIntervalMinutes = _config.ScanIntervalMinutes,
            ForceInitialScan = _config.ForceInitialScan,
            TimeZone = _config.TimeZone,
            Enabled = true
        };
    }

    public async Task<ScanType> DetermineScanTypeAsync()
    {
        if (_database == null)
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize first.");
        }

        var hostConfig = GetHostSpecificConfig();
        
        // Check if host-specific or global force initial scan is set
        if (hostConfig.ForceInitialScan)
        {
            _logger.LogInformation("Force initial scan enabled for host: {HostName}", _hostName);
            return ScanType.Initial;
        }
        
        // Check scan mode configuration
        if (hostConfig.ScanMode.Equals("Initial", StringComparison.OrdinalIgnoreCase))
        {
            return ScanType.Initial;
        }
        
        if (hostConfig.ScanMode.Equals("Incremental", StringComparison.OrdinalIgnoreCase))
        {
            return ScanType.Incremental;
        }
        
        // Auto mode - determine based on last scan
        var lastScanTime = await _database.GetLastScanEndTimeAsync(_hostName);
        if (lastScanTime == null)
        {
            _logger.LogInformation("No previous scan found for host: {HostName}, using Initial scan", _hostName);
            return ScanType.Initial;
        }
        
        _logger.LogInformation("Previous scan found for host: {HostName} at {LastScan}, using Incremental scan", _hostName, lastScanTime);
        return ScanType.Incremental;
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
        var inputFolderPath = _configuration["FileIndexerConfiguration:InputFolderPath"] ?? throw new InvalidOperationException("InputFolder not configured");
        var localDatabasePath = dbConfig["LocalDatabasePath"] ?? "fileindexer_local.db";
        
        Initialize(inputFolderPath, localDatabasePath);
    }

    public async Task IndexFilesAsync(CancellationToken cancellationToken = default)
    {
        var scanType = await DetermineScanTypeAsync();
        await IndexFilesAsync(scanType, cancellationToken);
    }

    public async Task IndexFilesAsync(ScanType scanType, CancellationToken cancellationToken = default)
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

        var scanStartTime = DateTime.Now;
        var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(_timeZone);
        var localScanStartTime = TimeZoneInfo.ConvertTime(scanStartTime, timeZoneInfo);
        
        _logger.LogInformation("Starting {ScanType} scan for folder: {InputFolder} at {StartTime} ({TimeZone})", 
            scanType, _inputFolderPath, localScanStartTime, _timeZone);

        DateTime? lastScanTime = null;
        if (scanType == ScanType.Incremental)
        {
            lastScanTime = await _database.GetLastScanEndTimeAsync(_hostName);
            if (lastScanTime == null)
            {
                _logger.LogWarning("Incremental scan requested but no previous scan found. Falling back to Initial scan.");
                scanType = ScanType.Initial;
            }
            else
            {
                _logger.LogInformation("Incremental scan will process files modified after: {LastScanTime}", lastScanTime);
            }
        }

        var files = Directory.GetFiles(_inputFolderPath, "*", SearchOption.AllDirectories);
        var processedCount = 0;
        var newFilesCount = 0;
        var updatedFilesCount = 0;
        var skippedCount = 0;

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
                
                // For incremental scan, skip files not modified since last scan
                if (scanType == ScanType.Incremental && lastScanTime.HasValue)
                {
                    if (fileInfo.LastWriteTime <= lastScanTime.Value)
                    {
                        skippedCount++;
                        continue;
                    }
                }
                
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

                // Log progress every batch
                if (processedCount % _config.BatchSize == 0)
                {
                    _logger.LogInformation("Processed {ProcessedCount} files so far... (New: {NewCount}, Updated: {UpdatedCount}, Skipped: {SkippedCount})", 
                        processedCount, newFilesCount, updatedFilesCount, skippedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error indexing file: {FilePath}", file);
            }
        }

        var scanEndTime = DateTime.Now;
        var localScanEndTime = TimeZoneInfo.ConvertTime(scanEndTime, timeZoneInfo);
        
        // Save scan metadata
        await _database.SaveScanMetadataAsync(_hostName, scanStartTime, scanEndTime, 
            scanType.ToString(), _timeZone, processedCount, newFilesCount, updatedFilesCount);

        _logger.LogInformation("{ScanType} scan completed in {Duration:F2} seconds. Processed: {ProcessedCount}, New: {NewCount}, Updated: {UpdatedCount}, Skipped: {SkippedCount}", 
            scanType, (scanEndTime - scanStartTime).TotalSeconds, processedCount, newFilesCount, updatedFilesCount, skippedCount);
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