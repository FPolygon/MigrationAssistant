using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using System.Diagnostics;
using MigrationTool.Service.Core;
using MigrationTool.Service.ProfileManagement;
using MigrationTool.Service.ProfileManagement.Native;

namespace MigrationTool.Service;

public class Program
{
    public static async Task Main(string[] args)
    {
        // Check if running installation commands
        if (args.Length > 0 && IsInstallCommand(args[0]))
        {
            Install.RunInstaller(args);
            return;
        }

        // Set up service directory as current directory
        var pathToExe = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(pathToExe))
        {
            var pathToContentRoot = Path.GetDirectoryName(pathToExe);
            if (!string.IsNullOrEmpty(pathToContentRoot))
            {
                Directory.SetCurrentDirectory(pathToContentRoot);
            }
        }

        // Early initialization of Serilog
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.EventLog("MigrationService", manageEventSource: true)
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting Migration Service...");

            var builder = Host.CreateDefaultBuilder(args);

            // Configure the service
            builder.UseWindowsService(options =>
            {
                options.ServiceName = "MigrationService";
            });

            // Configure Serilog as the logging provider
            builder.UseSerilog((context, services, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine("C:\\ProgramData\\MigrationTool\\Logs", "service-.txt"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.EventLog("MigrationService",
                    manageEventSource: true,
                    restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information));

            // Configure services
            builder.ConfigureServices((hostContext, services) =>
            {
                // Add configuration
                services.Configure<ServiceConfiguration>(
                    hostContext.Configuration.GetSection("ServiceConfiguration"));

                // Add hosted service
                services.AddHostedService<MigrationWindowsService>();

                // Add core services
                services.AddSingleton<IServiceManager, ServiceManager>();
                services.AddSingleton<IStateManager, StateManager>();
                services.AddSingleton<IIpcServer, IpcServer>();
                services.AddSingleton<IMigrationStateOrchestrator, MigrationStateOrchestrator>();
                
                // Add profile management services
                services.AddSingleton<IWindowsProfileRegistry, WindowsProfileRegistry>();
                services.AddSingleton<WindowsProfileDetector>();
                
                // Add activity detection services
                services.AddSingleton<WindowsActivityDetector>();
                services.AddSingleton<ProcessOwnershipDetector>();
                services.AddSingleton<FileActivityScanner>();
                services.AddSingleton<IActivityScoreCalculator, ActivityScoreCalculator>();
                
                // Add profile analysis services
                services.AddSingleton<IProfileActivityAnalyzer, ProfileActivityAnalyzer>();
                services.AddSingleton<IProfileClassifier, ProfileClassifier>();
                services.AddSingleton<IUserProfileManager, UserProfileManager>();
                
                // Add classification services
                services.AddSingleton<ClassificationRuleEngine>();
                services.AddSingleton<IClassificationOverrideManager, ClassificationOverrideManager>();
            });

            // Build and run the host
            var host = builder.Build();

            await host.RunAsync();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Migration Service terminated unexpectedly");
            throw;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static bool IsInstallCommand(string arg)
    {
        var installCommands = new[] { "install", "uninstall", "start", "stop", "status",
                                     "/install", "/uninstall", "/start", "/stop", "/status",
                                     "-install", "-uninstall", "-start", "-stop", "-status" };
        return installCommands.Contains(arg, StringComparer.OrdinalIgnoreCase);
    }
}

// Service configuration model
