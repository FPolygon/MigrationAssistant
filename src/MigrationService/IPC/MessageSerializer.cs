using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MigrationTool.Service.IPC.Messages;

namespace MigrationTool.Service.IPC;

public interface IMessageSerializer
{
    byte[] SerializeMessage(IpcMessage message);
    string SerializeMessageToString(IpcMessage message);
    IpcMessage? DeserializeMessage(byte[] data);
    IpcMessage? DeserializeMessage(string json);
    T? DeserializePayload<T>(object? payload) where T : class;
}

public class MessageSerializer : IMessageSerializer
{
    private readonly JsonSerializerOptions _options;

    public MessageSerializer()
    {
        _options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
    }

    public byte[] SerializeMessage(IpcMessage message)
    {
        var json = JsonSerializer.Serialize(message, _options);
        return Encoding.UTF8.GetBytes(json);
    }

    public string SerializeMessageToString(IpcMessage message)
    {
        return JsonSerializer.Serialize(message, _options);
    }

    public IpcMessage? DeserializeMessage(byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            return DeserializeMessage(json);
        }
        catch (Exception ex)
        {
            throw new MessageSerializationException("Failed to deserialize message from bytes", ex);
        }
    }

    public IpcMessage? DeserializeMessage(string json)
    {
        try
        {
            var message = JsonSerializer.Deserialize<IpcMessage>(json, _options);
            if (message == null)
            {
                throw new MessageSerializationException("Deserialized message was null");
            }

            // Deserialize the payload based on the message type
            if (message.Payload != null && message.Payload is JsonElement jsonElement)
            {
                message.Payload = DeserializePayloadByType(message.Type, jsonElement);
            }

            return message;
        }
        catch (JsonException ex)
        {
            throw new MessageSerializationException("Failed to deserialize message JSON", ex);
        }
    }

    public T? DeserializePayload<T>(object? payload) where T : class
    {
        if (payload == null)
        {
            return null;
        }

        if (payload is T typedPayload)
        {
            return typedPayload;
        }

        if (payload is JsonElement jsonElement)
        {
            return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), _options);
        }

        throw new MessageSerializationException($"Cannot deserialize payload of type {payload.GetType()} to {typeof(T)}");
    }

    private object? DeserializePayloadByType(string messageType, JsonElement jsonElement)
    {
        return messageType switch
        {
            // Service → Agent Messages
            MessageTypes.BackupRequest => JsonSerializer.Deserialize<BackupRequestPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.StatusUpdate => JsonSerializer.Deserialize<StatusUpdatePayload>(jsonElement.GetRawText(), _options),
            MessageTypes.EscalationNotice => JsonSerializer.Deserialize<EscalationNoticePayload>(jsonElement.GetRawText(), _options),
            MessageTypes.ConfigurationUpdate => JsonSerializer.Deserialize<ConfigurationUpdatePayload>(jsonElement.GetRawText(), _options),
            MessageTypes.ShutdownRequest => JsonSerializer.Deserialize<ShutdownRequestPayload>(jsonElement.GetRawText(), _options),

            // Agent → Service Messages
            MessageTypes.AgentStarted => JsonSerializer.Deserialize<AgentStartedPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.BackupStarted => JsonSerializer.Deserialize<BackupStartedPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.BackupProgress => JsonSerializer.Deserialize<BackupProgressPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.BackupCompleted => JsonSerializer.Deserialize<BackupCompletedPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.DelayRequest => JsonSerializer.Deserialize<DelayRequestPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.UserAction => JsonSerializer.Deserialize<UserActionPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.ErrorReport => JsonSerializer.Deserialize<ErrorReportPayload>(jsonElement.GetRawText(), _options),

            // Bidirectional Messages
            MessageTypes.Heartbeat => JsonSerializer.Deserialize<HeartbeatPayload>(jsonElement.GetRawText(), _options),
            MessageTypes.Acknowledgment => JsonSerializer.Deserialize<AcknowledgmentPayload>(jsonElement.GetRawText(), _options),

            _ => jsonElement
        };
    }
}

public class MessageSerializationException : Exception
{
    public MessageSerializationException(string message) : base(message) { }
    public MessageSerializationException(string message, Exception innerException) : base(message, innerException) { }
}