using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.ServiceProcess;
using System.Text;

namespace MigrationTool.Service;

[SupportedOSPlatform("windows")]
public static class ServiceInstaller
{
    private const string ServiceName = "MigrationService";
    private const string ServiceDisplayName = "Windows Migration Service";
    private const string ServiceDescription = "Manages user data migration to OneDrive before system reset for Autopilot enrollment";
    
    public static void Install(string exePath)
    {
        Console.WriteLine($"Installing {ServiceName}...");

        try
        {
            // Check if service already exists
            if (ServiceExists())
            {
                Console.WriteLine("Service already exists. Uninstalling first...");
                Uninstall();
            }

            // Create the service
            CreateService(exePath);

            // Configure recovery options
            ConfigureRecoveryOptions();

            // Set service description
            SetServiceDescription();

            Console.WriteLine($"{ServiceName} installed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to install service: {ex.Message}");
            throw;
        }
    }

    public static void Uninstall()
    {
        Console.WriteLine($"Uninstalling {ServiceName}...");

        try
        {
            // Stop the service if it's running
            StopService();

            // Delete the service
            DeleteService();

            Console.WriteLine($"{ServiceName} uninstalled successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to uninstall service: {ex.Message}");
            throw;
        }
    }

    private static bool ServiceExists()
    {
        using var serviceController = ServiceController.GetServices()
            .FirstOrDefault(s => s.ServiceName == ServiceName);
        return serviceController != null;
    }

    private static void CreateService(string exePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"create \"{ServiceName}\" " +
                       $"binPath= \"\\\"{exePath}\\\"\" " +
                       $"DisplayName= \"{ServiceDisplayName}\" " +
                       $"start= auto",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start sc.exe");
        }

        process.WaitForExit();
        
        if (process.ExitCode != 0)
        {
            var error = process.StandardError.ReadToEnd();
            throw new InvalidOperationException($"Failed to create service: {error}");
        }
    }

    private static void ConfigureRecoveryOptions()
    {
        // Configure service to restart on failure
        var recoveryActions = new StringBuilder();
        recoveryActions.Append("actions= restart/60000/restart/60000/restart/60000 "); // Restart after 1 minute
        recoveryActions.Append("reset= 86400"); // Reset failure count after 24 hours

        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"failure \"{ServiceName}\" {recoveryActions}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        process?.WaitForExit();

        // Set recovery to restart the service
        startInfo.Arguments = $"failureflag \"{ServiceName}\" 1";
        using var process2 = Process.Start(startInfo);
        process2?.WaitForExit();
    }

    private static void SetServiceDescription()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{ServiceName}", 
                writable: true);
            
            if (key != null)
            {
                key.SetValue("Description", ServiceDescription);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set service description: {ex.Message}");
            // Non-critical error, continue
        }
    }

    private static void StopService()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            
            if (service.Status == ServiceControllerStatus.Running)
            {
                Console.WriteLine("Stopping service...");
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to stop service: {ex.Message}");
            // Continue with uninstall even if stop fails
        }
    }

    private static void DeleteService()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"delete \"{ServiceName}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo);
        process?.WaitForExit();
    }

    // Additional helper methods for advanced configuration
    public static void SetDelayedAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{ServiceName}", 
                writable: true);
            
            if (key != null)
            {
                key.SetValue("DelayedAutostart", enabled ? 1 : 0, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set delayed auto-start: {ex.Message}");
        }
    }

    public static void SetServiceSidType()
    {
        // Set service SID type to restricted for additional security
        var startInfo = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"sidtype \"{ServiceName}\" restricted",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        process?.WaitForExit();
    }

    public static void GrantLogOnAsService(string accountName)
    {
        // This would require P/Invoke to advapi32.dll for LsaAddAccountRights
        // For now, we'll rely on the default LOCAL SYSTEM account
        Console.WriteLine($"Service will run as LOCAL SYSTEM account");
    }
}