using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MigrationTool.Service.IPC.Messages;

// Service → Agent Messages

public class BackupRequestPayload
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "normal";

    [JsonPropertyName("deadline")]
    public DateTime Deadline { get; set; }

    [JsonPropertyName("categories")]
    public List<string> Categories { get; set; } = new();
}

public class StatusUpdatePayload
{
    [JsonPropertyName("overallStatus")]
    public string OverallStatus { get; set; } = string.Empty;

    [JsonPropertyName("blockingUsers")]
    public List<string> BlockingUsers { get; set; } = new();

    [JsonPropertyName("readyUsers")]
    public List<string> ReadyUsers { get; set; } = new();

    [JsonPropertyName("totalUsers")]
    public int TotalUsers { get; set; }
}

public class EscalationNoticePayload
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("details")]
    public string Details { get; set; } = string.Empty;

    [JsonPropertyName("ticketNumber")]
    public string? TicketNumber { get; set; }
}

public class ConfigurationUpdatePayload
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = "user";
}

public class ShutdownRequestPayload
{
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    [JsonPropertyName("gracePeriodSeconds")]
    public int GracePeriodSeconds { get; set; } = 30;
}