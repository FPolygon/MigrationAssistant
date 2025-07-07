using System;
using MigrationTool.Service.IPC.Messages;

namespace MigrationTool.Service.IPC;

public static class MessageFactory
{
    // Service → Agent Messages

    public static IpcMessage<BackupRequestPayload> CreateBackupRequest(string userId, string priority, DateTime deadline, params string[] categories)
    {
        return new IpcMessage<BackupRequestPayload>
        {
            Type = MessageTypes.BackupRequest,
            Payload = new BackupRequestPayload
            {
                UserId = userId,
                Priority = priority,
                Deadline = deadline,
                Categories = new List<string>(categories)
            }
        };
    }

    public static IpcMessage<StatusUpdatePayload> CreateStatusUpdate(string overallStatus, List<string> blockingUsers, List<string> readyUsers, int totalUsers)
    {
        return new IpcMessage<StatusUpdatePayload>
        {
            Type = MessageTypes.StatusUpdate,
            Payload = new StatusUpdatePayload
            {
                OverallStatus = overallStatus,
                BlockingUsers = blockingUsers,
                ReadyUsers = readyUsers,
                TotalUsers = totalUsers
            }
        };
    }

    public static IpcMessage<EscalationNoticePayload> CreateEscalationNotice(string reason, string details, string? ticketNumber = null)
    {
        return new IpcMessage<EscalationNoticePayload>
        {
            Type = MessageTypes.EscalationNotice,
            Payload = new EscalationNoticePayload
            {
                Reason = reason,
                Details = details,
                TicketNumber = ticketNumber
            }
        };
    }

    // Agent → Service Messages

    public static IpcMessage<AgentStartedPayload> CreateAgentStarted(string userId, string agentVersion, string sessionId)
    {
        return new IpcMessage<AgentStartedPayload>
        {
            Type = MessageTypes.AgentStarted,
            Payload = new AgentStartedPayload
            {
                UserId = userId,
                AgentVersion = agentVersion,
                SessionId = sessionId
            }
        };
    }

    public static IpcMessage<BackupStartedPayload> CreateBackupStarted(string userId, List<string> categories, long estimatedSizeMB)
    {
        return new IpcMessage<BackupStartedPayload>
        {
            Type = MessageTypes.BackupStarted,
            Payload = new BackupStartedPayload
            {
                UserId = userId,
                Categories = categories,
                EstimatedSizeMB = estimatedSizeMB
            }
        };
    }

    public static IpcMessage<BackupProgressPayload> CreateBackupProgress(
        string userId,
        string category,
        double progress,
        long bytesTransferred,
        long bytesTotal,
        string? currentFile = null)
    {
        return new IpcMessage<BackupProgressPayload>
        {
            Type = MessageTypes.BackupProgress,
            Payload = new BackupProgressPayload
            {
                UserId = userId,
                Category = category,
                Progress = progress,
                BytesTransferred = bytesTransferred,
                BytesTotal = bytesTotal,
                CurrentFile = currentFile
            }
        };
    }

    public static IpcMessage<BackupCompletedPayload> CreateBackupCompleted(
        string userId,
        bool success,
        string? manifestPath,
        Dictionary<string, BackupCompletedPayload.CategoryResult> categories)
    {
        return new IpcMessage<BackupCompletedPayload>
        {
            Type = MessageTypes.BackupCompleted,
            Payload = new BackupCompletedPayload
            {
                UserId = userId,
                Success = success,
                ManifestPath = manifestPath,
                Categories = categories
            }
        };
    }

    public static IpcMessage<DelayRequestPayload> CreateDelayRequest(string userId, string reason, int requestedDelaySeconds, int delaysUsed)
    {
        return new IpcMessage<DelayRequestPayload>
        {
            Type = MessageTypes.DelayRequest,
            Payload = new DelayRequestPayload
            {
                UserId = userId,
                Reason = reason,
                RequestedDelaySeconds = requestedDelaySeconds,
                DelaysUsed = delaysUsed
            }
        };
    }

    // Bidirectional Messages

    public static IpcMessage<HeartbeatPayload> CreateHeartbeat(string senderId, long sequenceNumber)
    {
        return new IpcMessage<HeartbeatPayload>
        {
            Type = MessageTypes.Heartbeat,
            Payload = new HeartbeatPayload
            {
                SenderId = senderId,
                SequenceNumber = sequenceNumber,
                Timestamp = DateTime.UtcNow
            }
        };
    }

    public static IpcMessage<AcknowledgmentPayload> CreateAcknowledgment(string originalMessageId, bool success, string? error = null)
    {
        return new IpcMessage<AcknowledgmentPayload>
        {
            Type = MessageTypes.Acknowledgment,
            Payload = new AcknowledgmentPayload
            {
                OriginalMessageId = originalMessageId,
                Success = success,
                Error = error,
                Timestamp = DateTime.UtcNow
            }
        };
    }

    public static IpcMessage<ErrorReportPayload> CreateErrorReport(
        string userId,
        string errorCode,
        string message,
        string? stackTrace = null,
        Dictionary<string, object>? context = null)
    {
        return new IpcMessage<ErrorReportPayload>
        {
            Type = MessageTypes.ErrorReport,
            Payload = new ErrorReportPayload
            {
                UserId = userId,
                ErrorCode = errorCode,
                Message = message,
                StackTrace = stackTrace,
                Context = context ?? new Dictionary<string, object>()
            }
        };
    }
}