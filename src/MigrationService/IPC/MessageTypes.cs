namespace MigrationTool.Service.IPC;

public static class MessageTypes
{
    // Service → Agent Messages
    public const string BackupRequest = "BACKUP_REQUEST";
    public const string StatusUpdate = "STATUS_UPDATE";
    public const string EscalationNotice = "ESCALATION_NOTICE";
    public const string ConfigurationUpdate = "CONFIGURATION_UPDATE";
    public const string ShutdownRequest = "SHUTDOWN_REQUEST";
    
    // Agent → Service Messages
    public const string AgentStarted = "AGENT_STARTED";
    public const string BackupStarted = "BACKUP_STARTED";
    public const string BackupProgress = "BACKUP_PROGRESS";
    public const string BackupCompleted = "BACKUP_COMPLETED";
    public const string DelayRequest = "DELAY_REQUEST";
    public const string UserAction = "USER_ACTION";
    public const string ErrorReport = "ERROR_REPORT";
    
    // Bidirectional Messages
    public const string Heartbeat = "HEARTBEAT";
    public const string Acknowledgment = "ACKNOWLEDGMENT";
}