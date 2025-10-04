using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Models.SharedToLocal;
using Shared.Configuration;

namespace Shared.Services;

public interface IFileSyncService
{
    Task SyncFilesAsync(CancellationToken cancellationToken = default);
    Task InitializeAsync();
    void Initialize(SharedToLocalConfiguration config);
}

public class FileSyncService : IFileSyncService
{
    private readonly ILogger<FileSyncService> _logger;
    private readonly IConfiguration _configuration;
    private SharedToLocalConfiguration _config;
    private string _hostFolderPath = string.Empty;

    public FileSyncService(ILogger<FileSyncService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _config = new SharedToLocalConfiguration();
    }

    public void Initialize(SharedToLocalConfiguration config)
    {
        _config = config;
        var hostName = Environment.MachineName;
        _hostFolderPath = Path.Combine(_config.SharedFolderPath, hostName);
    }

    public async Task InitializeAsync()
    {
        _configuration.GetSection(SharedToLocalConfiguration.SectionName).Bind(_config);
        Initialize(_config);
        await Task.CompletedTask;
    }

    public async Task SyncFilesAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_hostFolderPath))
        {
            throw new InvalidOperationException("Service not initialized. Call Initialize first.");
        }

        var changesSent = await SendChangesToSharedAsync(cancellationToken);
        var changesReceived = await ReceiveChangesFromSharedAsync(cancellationToken);

        _logger.LogInformation("Sync completed - Changes sent: {ChangesSent}, Changes received: {ChangesReceived}", 
            changesSent, changesReceived);
    }

    private async Task<int> SendChangesToSharedAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(_config.LocalFolderPath))
            {
                _logger.LogWarning("Local folder does not exist: {LocalPath}", _config.LocalFolderPath);
                return 0;
            }

            // Ensure host folder exists in shared location
            if (!Directory.Exists(_hostFolderPath))
            {
                Directory.CreateDirectory(_hostFolderPath);
                _logger.LogInformation("Created host folder: {HostPath}", _hostFolderPath);
            }

            var changesSent = 0;
            var localFiles = Directory.GetFiles(_config.LocalFolderPath, "*", SearchOption.AllDirectories);

            foreach (var localFile in localFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var relativePath = Path.GetRelativePath(_config.LocalFolderPath, localFile);
                    var sharedFile = Path.Combine(_hostFolderPath, relativePath);
                    var sharedDir = Path.GetDirectoryName(sharedFile);

                    if (!string.IsNullOrEmpty(sharedDir) && !Directory.Exists(sharedDir))
                    {
                        Directory.CreateDirectory(sharedDir);
                    }

                    var localInfo = new FileInfo(localFile);
                    var shouldCopy = !File.Exists(sharedFile);

                    if (!shouldCopy)
                    {
                        var sharedInfo = new FileInfo(sharedFile);
                        shouldCopy = localInfo.LastWriteTime > sharedInfo.LastWriteTime ||
                                   localInfo.Length != sharedInfo.Length;
                    }

                    if (shouldCopy)
                    {
                        File.Copy(localFile, sharedFile, true);
                        changesSent++;
                        
                        var changeInfo = new FileChangeInfo
                        {
                            RelativePath = relativePath,
                            ChangeType = File.Exists(sharedFile) ? FileChangeType.Modified : FileChangeType.Created,
                            Timestamp = DateTime.Now
                        };

                        _logger.LogDebug("Sent change: {ChangeType} - {RelativePath}", changeInfo.ChangeType, relativePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending file to shared: {LocalFile}", localFile);
                }
            }

            _logger.LogInformation("Sent {ChangesSent} changes to shared folder", changesSent);
            return changesSent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during send to shared operation");
            return 0;
        }
    }

    private async Task<int> ReceiveChangesFromSharedAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!Directory.Exists(_hostFolderPath))
            {
                _logger.LogInformation("Host folder does not exist in shared location: {HostPath}", _hostFolderPath);
                return 0;
            }

            if (!Directory.Exists(_config.LocalFolderPath))
            {
                Directory.CreateDirectory(_config.LocalFolderPath);
                _logger.LogInformation("Created local folder: {LocalPath}", _config.LocalFolderPath);
            }

            var changesReceived = 0;
            var sharedFiles = Directory.GetFiles(_hostFolderPath, "*", SearchOption.AllDirectories);

            foreach (var sharedFile in sharedFiles)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var relativePath = Path.GetRelativePath(_hostFolderPath, sharedFile);
                    var localFile = Path.Combine(_config.LocalFolderPath, relativePath);
                    var localDir = Path.GetDirectoryName(localFile);

                    if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                    {
                        Directory.CreateDirectory(localDir);
                    }

                    var sharedInfo = new FileInfo(sharedFile);
                    var shouldCopy = !File.Exists(localFile);

                    if (!shouldCopy)
                    {
                        var localInfo = new FileInfo(localFile);
                        shouldCopy = sharedInfo.LastWriteTime > localInfo.LastWriteTime ||
                                   sharedInfo.Length != localInfo.Length;
                    }

                    if (shouldCopy)
                    {
                        File.Copy(sharedFile, localFile, true);
                        changesReceived++;
                        
                        var changeInfo = new FileChangeInfo
                        {
                            RelativePath = relativePath,
                            ChangeType = File.Exists(localFile) ? FileChangeType.Modified : FileChangeType.Created,
                            Timestamp = DateTime.Now
                        };

                        _logger.LogDebug("Received change: {ChangeType} - {RelativePath}", changeInfo.ChangeType, relativePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error receiving file from shared: {SharedFile}", sharedFile);
                }
            }

            _logger.LogInformation("Received {ChangesReceived} changes from shared folder", changesReceived);
            return changesReceived;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during receive from shared operation");
            return 0;
        }
    }
}