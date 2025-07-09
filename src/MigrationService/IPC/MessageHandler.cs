using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.IPC;

public abstract class MessageHandler<TPayload> : IMessageHandler<TPayload> where TPayload : class
{
    protected readonly ILogger Logger;
    protected readonly IMessageSerializer Serializer;

    public abstract string MessageType { get; }

    protected MessageHandler(ILogger logger, IMessageSerializer serializer)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public async Task<IpcMessage?> HandleAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (message.Type != MessageType)
        {
            throw new InvalidOperationException($"Handler for {MessageType} cannot handle message type {message.Type}");
        }

        var payload = Serializer.DeserializePayload<TPayload>(message.Payload);

        if (payload == null)
        {
            Logger.LogWarning("Failed to deserialize payload for message {MessageId} of type {MessageType}",
                message.Id, message.Type);

            return MessageFactory.CreateAcknowledgment(message.Id, false, "Failed to deserialize payload");
        }

        try
        {
            return await HandleAsync(clientId, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error handling message {MessageId} of type {MessageType}",
                message.Id, message.Type);

            return MessageFactory.CreateAcknowledgment(message.Id, false, ex.Message);
        }
    }

    public abstract Task<IpcMessage?> HandleAsync(string clientId, TPayload payload, CancellationToken cancellationToken = default);
}
