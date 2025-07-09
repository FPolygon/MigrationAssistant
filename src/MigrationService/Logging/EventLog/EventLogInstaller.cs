using System;
using System.Diagnostics;
using System.Security;

namespace MigrationTool.Service.Logging.EventLog;

/// <summary>
/// Utility for installing and uninstalling Windows Event Log sources.
/// This class requires administrator privileges to create/delete event sources.
/// </summary>
public static class EventLogInstaller
{
    /// <summary>
    /// Creates the event source for the Migration Tool if it doesn't exist.
    /// </summary>
    /// <param name="sourceName">The name of the event source.</param>
    /// <param name="logName">The name of the event log.</param>
    /// <returns>True if the source was created or already exists; otherwise, false.</returns>
    public static bool CreateEventSource(string sourceName = "MigrationTool", string logName = "Application")
    {
        try
        {
            // Check if source already exists
            if (System.Diagnostics.EventLog.SourceExists(sourceName))
            {
                // Verify it's associated with the correct log
                var existingLog = System.Diagnostics.EventLog.LogNameFromSourceName(sourceName, ".");
                if (string.Equals(existingLog, logName, StringComparison.OrdinalIgnoreCase))
                {
                    return true; // Source exists and is correctly configured
                }
                else
                {
                    Console.WriteLine($"Warning: Event source '{sourceName}' exists but is associated with '{existingLog}' instead of '{logName}'");
                    return false;
                }
            }

            // Create the event source
            var sourceData = new EventSourceCreationData(sourceName, logName)
            {
                MachineName = "."
            };

            System.Diagnostics.EventLog.CreateEventSource(sourceData);

            Console.WriteLine($"Successfully created event source '{sourceName}' in log '{logName}'");
            return true;
        }
        catch (SecurityException ex)
        {
            Console.WriteLine($"Failed to create event source '{sourceName}': Insufficient privileges. {ex.Message}");
            Console.WriteLine("Run as administrator to create the event source.");
            return false;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Failed to create event source '{sourceName}': Invalid argument. {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create event source '{sourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes the event source for the Migration Tool.
    /// </summary>
    /// <param name="sourceName">The name of the event source to remove.</param>
    /// <returns>True if the source was removed or doesn't exist; otherwise, false.</returns>
    public static bool RemoveEventSource(string sourceName = "MigrationTool")
    {
        try
        {
            if (!System.Diagnostics.EventLog.SourceExists(sourceName))
            {
                return true; // Source doesn't exist, nothing to remove
            }

            System.Diagnostics.EventLog.DeleteEventSource(sourceName);

            Console.WriteLine($"Successfully removed event source '{sourceName}'");
            return true;
        }
        catch (SecurityException ex)
        {
            Console.WriteLine($"Failed to remove event source '{sourceName}': Insufficient privileges. {ex.Message}");
            Console.WriteLine("Run as administrator to remove the event source.");
            return false;
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine($"Failed to remove event source '{sourceName}': Invalid argument. {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to remove event source '{sourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verifies that the event source exists and is properly configured.
    /// </summary>
    /// <param name="sourceName">The name of the event source.</param>
    /// <param name="expectedLogName">The expected log name.</param>
    /// <returns>True if the source exists and is properly configured; otherwise, false.</returns>
    public static bool VerifyEventSource(string sourceName = "MigrationTool", string expectedLogName = "Application")
    {
        try
        {
            if (!System.Diagnostics.EventLog.SourceExists(sourceName))
            {
                Console.WriteLine($"Event source '{sourceName}' does not exist");
                return false;
            }

            var actualLogName = System.Diagnostics.EventLog.LogNameFromSourceName(sourceName, ".");
            if (!string.Equals(actualLogName, expectedLogName, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Event source '{sourceName}' is associated with '{actualLogName}' instead of expected '{expectedLogName}'");
                return false;
            }

            Console.WriteLine($"Event source '{sourceName}' is properly configured for log '{expectedLogName}'");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to verify event source '{sourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Lists all event sources in the specified log.
    /// </summary>
    /// <param name="logName">The name of the event log.</param>
    public static void ListEventSources(string logName = "Application")
    {
        try
        {
            var eventLog = new System.Diagnostics.EventLog(logName);

            Console.WriteLine($"Event sources in '{logName}' log:");
            Console.WriteLine(new string('-', 50));

            // Note: There's no direct API to list all sources in a log
            // This is a simplified approach
            Console.WriteLine("Use Windows Event Viewer or PowerShell Get-WinEvent to see all sources");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to list event sources for log '{logName}': {ex.Message}");
        }
    }

    /// <summary>
    /// Tests writing a test event to verify the event source is working.
    /// </summary>
    /// <param name="sourceName">The name of the event source.</param>
    /// <returns>True if the test event was written successfully; otherwise, false.</returns>
    public static bool TestEventSource(string sourceName = "MigrationTool")
    {
        try
        {
            if (!System.Diagnostics.EventLog.SourceExists(sourceName))
            {
                Console.WriteLine($"Event source '{sourceName}' does not exist");
                return false;
            }

            using var eventLog = new System.Diagnostics.EventLog();
            eventLog.Source = sourceName;

            var testMessage = $"Test event from MigrationTool logging system at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";
            eventLog.WriteEntry(testMessage, EventLogEntryType.Information, EventIdMapper.SpecialEventIds.ConfigurationLoaded);

            Console.WriteLine($"Successfully wrote test event to source '{sourceName}'");
            Console.WriteLine("Check Windows Event Viewer to verify the event was logged");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write test event to source '{sourceName}': {ex.Message}");
            return false;
        }
    }
}
