using FileIndexerService.Services;
using Microsoft.Extensions.Logging.EventLog;

var builder = Host.CreateApplicationBuilder(args);

// Configure for Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "FileIndexerService";
});

// Add logging
builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
    configure.AddDebug();
    
    // Add Event Log for Windows Service
    if (OperatingSystem.IsWindows())
    {
        configure.AddEventLog(new EventLogSettings
        {
            SourceName = "FileIndexerService",
            LogName = "Application"
        });
    }
});

// Add the main services
builder.Services.AddHostedService<FileIndexerService.Services.FileIndexerService>();
builder.Services.AddHostedService<FileIndexerService.Services.DeleteIsActiveFilesService>();

var host = builder.Build();

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    // Get logger from DI container for critical errors
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogCritical(ex, "Application terminated unexpectedly");
    throw;
}
