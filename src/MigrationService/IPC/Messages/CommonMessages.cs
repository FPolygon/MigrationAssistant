using System;
using System.Text.Json.Serialization;

namespace MigrationTool.Service.IPC.Messages;

// Bidirectional Messages

public class HeartbeatPayload
{
    [JsonPropertyName("senderId")]
    public string SenderId { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("sequenceNumber")]
    public long SequenceNumber { get; set; }
}

public class AcknowledgmentPayload
{
    [JsonPropertyName("originalMessageId")]
    public string OriginalMessageId { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
