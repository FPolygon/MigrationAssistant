namespace MigrationTool.Service;

public class Install
{
    public static void RunInstaller(string[] args)
    {
        if (args.Length == 0)
        {
            ShowUsage();
            return;
        }

        try
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            // If running from a dll, change to exe
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                exePath = exePath.Substring(0, exePath.Length - 4) + ".exe";
            }

            switch (args[0].ToLower())
            {
                case "install":
                case "/install":
                case "-install":
                    ServiceInstaller.Install(exePath);
                    break;

                case "uninstall":
                case "/uninstall":
                case "-uninstall":
                    ServiceInstaller.Uninstall();
                    break;

                case "start":
                case "/start":
                case "-start":
                    StartService();
                    break;

                case "stop":
                case "/stop":
                case "-stop":
                    StopService();
                    break;

                case "status":
                case "/status":
                case "-status":
                    ShowServiceStatus();
                    break;

                default:
                    Console.WriteLine($"Unknown command: {args[0]}");
                    ShowUsage();
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static void ShowUsage()
    {
        Console.WriteLine("Migration Service Installer");
        Console.WriteLine();
        Console.WriteLine("Usage: MigrationService.exe [command]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  install    - Install the service");
        Console.WriteLine("  uninstall  - Uninstall the service");
        Console.WriteLine("  start      - Start the service");
        Console.WriteLine("  stop       - Stop the service");
        Console.WriteLine("  status     - Show service status");
        Console.WriteLine();
        Console.WriteLine("Note: Administrative privileges are required for all operations");
    }

    private static void StartService()
    {
        using var service = new System.ServiceProcess.ServiceController("MigrationService");

        if (service.Status == System.ServiceProcess.ServiceControllerStatus.Running)
        {
            Console.WriteLine("Service is already running");
            return;
        }

        Console.WriteLine("Starting service...");
        service.Start();
        service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        Console.WriteLine("Service started successfully");
    }

    private static void StopService()
    {
        using var service = new System.ServiceProcess.ServiceController("MigrationService");

        if (service.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            Console.WriteLine("Service is already stopped");
            return;
        }

        Console.WriteLine("Stopping service...");
        service.Stop();
        service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        Console.WriteLine("Service stopped successfully");
    }

    private static void ShowServiceStatus()
    {
        try
        {
            using var service = new System.ServiceProcess.ServiceController("MigrationService");
            Console.WriteLine($"Service Name: {service.ServiceName}");
            Console.WriteLine($"Display Name: {service.DisplayName}");
            Console.WriteLine($"Status: {service.Status}");
            Console.WriteLine($"Can Stop: {service.CanStop}");
            Console.WriteLine($"Can Pause: {service.CanPauseAndContinue}");
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("Service is not installed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking service status: {ex.Message}");
        }
    }
}
