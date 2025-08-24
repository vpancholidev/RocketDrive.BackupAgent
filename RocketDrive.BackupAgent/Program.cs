using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RocketDrive.BackupAgent.Services;
using Serilog;
class Program
{

    static void Main(string[] args)
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.Async(a => a.File(
        "logs\\log-.json",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,                 // allowed with Async
        fileSizeLimitBytes: 10_000_000,
        rollOnFileSizeLimit: true))
    .CreateLogger();
            // Load configuration (appsettings.json)
            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Setup Dependency Injection
            var serviceProvider = new ServiceCollection()
                .AddSingleton<IConfiguration>(config)

                // Register services
                .AddSingleton<GoogleDriveService>()
                .AddSingleton<BackupService>()
                .AddSingleton<INotifier, EmailNotifier>()
                .AddSingleton<INotifier, TelegramNotifier>()
                .AddSingleton<CompositeNotifier>()
                .BuildServiceProvider();


            Log.Information("RocketDrive starting...");
            // Run the backup

            var backupService = serviceProvider.GetRequiredService<BackupService>();
            backupService.RunBackup();

            Console.WriteLine("✅ Backup completed. Press any key to exit...");
            Log.Information("RocketDrive Ended...");
            Console.ReadKey();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Top-level error: {ex}");
            try { Directory.CreateDirectory("logs"); File.AppendAllText("logs\\fatal.log", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}"); } catch { }
        }
        finally
        {
            Console.ReadKey();
        }
    }
}
