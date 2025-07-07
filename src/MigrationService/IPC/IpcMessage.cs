using System;
using System.Text.Json.Serialization;

namespace MigrationTool.Service.IPC;

public class IpcMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
}

public class IpcMessage<T> : IpcMessage where T : class
{
    [JsonPropertyName("payload")]
    public new T? Payload
    {
        get => base.Payload as T;
        set => base.Payload = value;
    }
}