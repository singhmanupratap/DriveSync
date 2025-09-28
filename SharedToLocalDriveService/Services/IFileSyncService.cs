using SharedToLocalDriveService.Models;

namespace SharedToLocalDriveService.Services;

public interface IFileSyncService
{
    Task SyncFilesAsync(CancellationToken cancellationToken = default);
    Task ProcessFileChangeAsync(FileChangeInfo fileChange, CancellationToken cancellationToken = default);
}