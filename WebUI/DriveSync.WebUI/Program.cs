using DriveSync.Shared.Data;
using DriveSync.WebUI.Services;

namespace DriveSync.WebUI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddControllersWithViews();

            // Configure graceful shutdown
            builder.Services.Configure<HostOptions>(opts => {
                opts.ShutdownTimeout = TimeSpan.FromSeconds(30); // Allow 30 seconds for graceful shutdown
            });

            // Configuration for database path
            builder.Services.Configure<DatabaseConfig>(builder.Configuration.GetSection("DatabaseConfig"));

            // Register database copy service
            builder.Services.AddScoped<DatabaseCopyService>();

            // Register background service for periodic database sync
            builder.Services.AddHostedService<DatabaseSyncBackgroundService>();

            // Register database service
            builder.Services.AddScoped<FileIndexerDatabase>(serviceProvider =>
            {
                var config = builder.Configuration.GetSection("DatabaseConfig");
                var localDatabasePath = config["LocalDatabasePath"] ?? "fileindexer_local.db";
                var connectionString = $"Data Source={localDatabasePath}";
                return new FileIndexerDatabase(connectionString);
            });

            var app = builder.Build();

            // Register shutdown handlers for database sync
            var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() => {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Application stopping - initiating final database sync");
            });

            // Handle process exit events
            AppDomain.CurrentDomain.ProcessExit += async (sender, e) => {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    logger.LogInformation("Process exit detected - performing emergency database sync");
                    using var scope = app.Services.CreateScope();
                    var databaseCopyService = scope.ServiceProvider.GetRequiredService<DatabaseCopyService>();
                    await databaseCopyService.SyncAllChangesToRemoteAsync();
                    logger.LogInformation("Emergency database sync completed");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during emergency database sync");
                }
            };

            Console.CancelKeyPress += async (sender, e) => {
                var logger = app.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    logger.LogInformation("Ctrl+C detected - performing final database sync");
                    e.Cancel = true; // Prevent immediate termination
                    
                    using var scope = app.Services.CreateScope();
                    var databaseCopyService = scope.ServiceProvider.GetRequiredService<DatabaseCopyService>();
                    await databaseCopyService.SyncAllChangesToRemoteAsync();
                    logger.LogInformation("Final database sync completed - shutting down gracefully");
                    
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during Ctrl+C database sync");
                    Environment.Exit(1);
                }
            };

            // Copy database on startup
            using (var scope = app.Services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var databaseCopyService = scope.ServiceProvider.GetRequiredService<DatabaseCopyService>();
                
                try
                {
                    logger.LogInformation("Starting database copy process...");
                    await databaseCopyService.CopyDatabaseToLocalAsync();
                    logger.LogInformation("Database copy process completed");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error during database startup process");
                    // Continue anyway - the app should still work with an empty database
                }
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();

            app.UseRouting();

            app.UseAuthorization();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Files}/{action=Index}/{id?}");

            await app.RunAsync();
        }
    }

    public class DatabaseConfig
    {
        public string DatabasePath { get; set; } = string.Empty;
        public string LocalDatabasePath { get; set; } = "fileindexer_local.db";
    }
}
