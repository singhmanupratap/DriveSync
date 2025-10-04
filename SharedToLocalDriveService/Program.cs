using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Shared.Services;
using Shared.Configuration;
using SharedToLocalDriveService.BackgroundServices;

namespace SharedToLocalDriveService;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            // Log startup errors
            var logger = host.Services.GetService<ILogger<Program>>();
            logger?.LogCritical(ex, "Application terminated unexpectedly");
            throw;
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "SharedToLocalDriveService";
            })
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                config.AddEnvironmentVariables();
                config.AddCommandLine(args);
            })
            .ConfigureServices((context, services) =>
            {
                // Configuration
                services.Configure<SharedToLocalConfiguration>(
                    context.Configuration.GetSection(SharedToLocalConfiguration.SectionName));

                // Register services
                services.AddScoped<IFileSyncService, FileSyncService>();

                // Services - Use the new background service
                services.AddHostedService<SharedToLocalDriveBackgroundService>();
            })
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                
                if (OperatingSystem.IsWindows())
                {
                    logging.AddEventLog(options =>
                    {
                        if (OperatingSystem.IsWindows())
                        {
                            options.SourceName = "SharedToLocalDriveService";
                            options.LogName = "Application";
                        }
                    });
                }
                
                logging.AddConsole();
                logging.AddDebug();

                // Set minimum log level
                logging.SetMinimumLevel(LogLevel.Information);
            });
}