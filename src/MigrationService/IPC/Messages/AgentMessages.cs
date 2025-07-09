using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MigrationTool.Service.IPC.Messages;

// Agent → Service Messages

public class AgentStartedPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("agentVersion")]
    public string AgentVersion { get; set; } = string.Empty;

    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;
}

public class BackupStartedPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();

    [JsonPropertyName("estimatedSizeMB")]
    public long EstimatedSizeMB { get; set; }
}

public class BackupProgressPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("currentFile")]
    public string? CurrentFile { get; set; }

    [JsonPropertyName("bytesTransferred")]
    public long BytesTransferred { get; set; }

    [JsonPropertyName("bytesTotal")]
    public long BytesTotal { get; set; }
}

public class BackupCompletedPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("manifestPath")]
    public string? ManifestPath { get; set; }

    [JsonPropertyName("categories")]
    public Dictionary<string, CategoryResult> Categories { get; set; } = new();

    public class CategoryResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("itemCount")]
        public int ItemCount { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}

public class DelayRequestPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("requestedDelay")]
    public int RequestedDelaySeconds { get; set; }

    [JsonPropertyName("delaysUsed")]
    public int DelaysUsed { get; set; }
}

public class UserActionPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public Dictionary<string, object> Details { get; set; } = new();
}

public class ErrorReportPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; set; }

    [JsonPropertyName("context")]
    public Dictionary<string, object> Context { get; set; } = new();
}
