using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedToLocalDriveService.Configuration;
using SharedToLocalDriveService.Models;
using System.IO;

namespace SharedToLocalDriveService.Services;

public class FileSyncService : IFileSyncService
{
    private readonly ILogger<FileSyncService> _logger;
    private readonly ServiceConfiguration _config;
    private readonly string _hostFolderPath;

    public FileSyncService(ILogger<FileSyncService> logger, IOptions<ServiceConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        
        var hostName = Environment.MachineName;
        _hostFolderPath = Path.Combine(_config.SharedFolderPath, hostName);
    }

    public async Task SyncFilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting file synchronization from {SharedPath} to {LocalPath}", 
                _hostFolderPath, _config.LocalFolderPath);

            // Ensure the host folder exists
            if (!Directory.Exists(_hostFolderPath))
            {
                _logger.LogInformation("Creating host folder: {HostFolder}", _hostFolderPath);
                Directory.CreateDirectory(_hostFolderPath);
            }

            // Ensure local folder exists
            if (!Directory.Exists(_config.LocalFolderPath))
            {
                _logger.LogInformation("Creating local folder: {LocalFolder}", _config.LocalFolderPath);
                Directory.CreateDirectory(_config.LocalFolderPath);
            }

            // Process all files in the shared folder
            await ProcessDirectoryAsync(_hostFolderPath, cancellationToken);

            _logger.LogInformation("File synchronization completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during file synchronization");
            throw;
        }
    }

    private async Task ProcessDirectoryAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directoryPath))
        {
            _logger.LogWarning("Directory does not exist: {Directory}", directoryPath);
            return;
        }

        try
        {
            // Process files
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var relativePath = Path.GetRelativePath(_hostFolderPath, file);
                var fileInfo = new FileInfo(file);

                var fileChange = new FileChangeInfo
                {
                    FilePath = file,
                    RelativePath = relativePath,
                    ChangeType = FileChangeType.Created,
                    Timestamp = fileInfo.LastWriteTimeUtc,
                    IsDeleteOperation = file.EndsWith(".delete", StringComparison.OrdinalIgnoreCase)
                };

                await ProcessFileChangeAsync(fileChange, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing directory: {Directory}", directoryPath);
        }
    }

    public async Task ProcessFileChangeAsync(FileChangeInfo fileChange, CancellationToken cancellationToken = default)
    {
        try
        {
            if (fileChange.IsDeleteOperation)
            {
                await ProcessDeleteOperationAsync(fileChange, cancellationToken);
            }
            else
            {
                await ProcessCopyOperationAsync(fileChange, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file change: {FilePath}", fileChange.FilePath);
        }
    }

    private void DeleteEmptyDirectoriesRecursively(string directoryPath, string rootPath)
    {
        try
        {
            if (string.IsNullOrEmpty(directoryPath) || !Directory.Exists(directoryPath))
                return;

            // Don't delete the root path itself
            if (string.Equals(directoryPath, rootPath, StringComparison.OrdinalIgnoreCase))
                return;

            // Check if directory is empty (no files and no subdirectories with content)
            if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
            {
                _logger.LogInformation("Deleting empty directory: {Directory}", directoryPath);
                Directory.Delete(directoryPath);
                
                // Recursively check parent directory
                var parentDirectory = Path.GetDirectoryName(directoryPath);
                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    DeleteEmptyDirectoriesRecursively(parentDirectory, rootPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting empty directory: {Directory}", directoryPath);
        }
    }

    private async Task ProcessDeleteOperationAsync(FileChangeInfo fileChange, CancellationToken cancellationToken)
    {
        try
        {
            // Remove .delete extension to get the actual file name
            var actualFileName = fileChange.RelativePath;
            if (actualFileName.EndsWith(".delete", StringComparison.OrdinalIgnoreCase))
            {
                actualFileName = actualFileName[..^7]; // Remove .delete (7 characters)
            }

            var targetPath = Path.Combine(_config.LocalFolderPath, actualFileName);

            if (File.Exists(targetPath))
            {
                _logger.LogInformation("Deleting file: {TargetPath}", targetPath);
                File.Delete(targetPath);
                
                // Recursively delete empty directories
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    DeleteEmptyDirectoriesRecursively(directory, _config.LocalFolderPath);
                }
            }
            else
            {
                _logger.LogInformation("Target file does not exist, nothing to delete: {TargetPath}", targetPath);
            }

            // Remove the source .delete file
            if (File.Exists(fileChange.FilePath))
            {
                _logger.LogInformation("Removing source delete file: {SourcePath}", fileChange.FilePath);
                File.Delete(fileChange.FilePath);
                
                // Recursively delete empty directories from source
                var sourceDirectory = Path.GetDirectoryName(fileChange.FilePath);
                if (!string.IsNullOrEmpty(sourceDirectory))
                {
                    DeleteEmptyDirectoriesRecursively(sourceDirectory, _hostFolderPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing delete operation for file: {FilePath}", fileChange.FilePath);
        }
    }

    private async Task ProcessCopyOperationAsync(FileChangeInfo fileChange, CancellationToken cancellationToken)
    {
        try
        {
            var targetPath = Path.Combine(_config.LocalFolderPath, fileChange.RelativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);

            // Ensure target directory exists
            if (!string.IsNullOrEmpty(targetDirectory) && !Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                _logger.LogInformation("Created directory: {Directory}", targetDirectory);
            }

            // Check if target file exists and compare timestamps
            bool shouldCopy = true;
            if (File.Exists(targetPath))
            {
                var sourceInfo = new FileInfo(fileChange.FilePath);
                var targetInfo = new FileInfo(targetPath);

                // Convert to UTC for comparison considering timezone
                var sourceTimeUtc = sourceInfo.LastWriteTimeUtc;
                var targetTimeUtc = targetInfo.LastWriteTimeUtc;

                if (sourceTimeUtc <= targetTimeUtc && sourceInfo.Length == targetInfo.Length)
                {
                    shouldCopy = false;
                    _logger.LogDebug("File is up to date: {TargetPath}", targetPath);
                }
            }

            if (shouldCopy)
            {
                _logger.LogInformation("Moving file from {SourcePath} to {TargetPath}", fileChange.FilePath, targetPath);
                
                // Copy file with attributes and timestamps
                File.Copy(fileChange.FilePath, targetPath, overwrite: true);
                
                // Preserve timestamps
                var sourceInfo = new FileInfo(fileChange.FilePath);
                var targetInfo = new FileInfo(targetPath);
                targetInfo.CreationTimeUtc = sourceInfo.CreationTimeUtc;
                targetInfo.LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
                targetInfo.LastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
                
                // Delete source file after successful copy (move operation)
                _logger.LogInformation("Deleting source file after successful move: {SourcePath}", fileChange.FilePath);
                File.Delete(fileChange.FilePath);
                
                // Recursively delete empty directories from source
                var sourceDirectory = Path.GetDirectoryName(fileChange.FilePath);
                if (!string.IsNullOrEmpty(sourceDirectory))
                {
                    DeleteEmptyDirectoriesRecursively(sourceDirectory, _hostFolderPath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying file from {SourcePath} to target location", fileChange.FilePath);
        }
    }
}