namespace Shared.Services;

public interface IHostOrchestrationService
{
    /// <summary>
    /// Gets the startup delay for a specific service on the current host
    /// </summary>
    /// <param name="serviceName">Name of the service (e.g., "FileIndexer", "DeleteInactive")</param>
    /// <returns>Time to delay before starting the service</returns>
    Task<TimeSpan> GetStartupDelayAsync(string serviceName);
    
    /// <summary>
    /// Gets the staggered interval for a specific service
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <param name="baseInterval">Base interval in minutes</param>
    /// <returns>Adjusted interval for staggered execution</returns>
    Task<int> GetStaggeredIntervalAsync(string serviceName, int baseInterval);
    
    /// <summary>
    /// Checks if a service is enabled for the current host
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <returns>True if service is enabled for this host</returns>
    bool IsServiceEnabledForHost(string serviceName);
    
    /// <summary>
    /// Checks if the service can start now without conflicting with other hosts
    /// </summary>
    /// <param name="serviceName">Name of the service</param>
    /// <returns>True if service can start safely now</returns>
    Task<bool> CanServiceStartNowAsync(string serviceName);
}