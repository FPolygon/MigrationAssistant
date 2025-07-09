namespace MigrationTool.Service;

public class ServiceConfiguration
{
    public string DataPath { get; set; } = "C:\\ProgramData\\MigrationTool\\Data";
    public string LogPath { get; set; } = "C:\\ProgramData\\MigrationTool\\Logs";
    public string PipeName { get; set; } = "MigrationService_{ComputerName}";
    public int StateCheckIntervalSeconds { get; set; } = 300; // 5 minutes
    public bool EnableDebugLogging { get; set; } = false;
}
