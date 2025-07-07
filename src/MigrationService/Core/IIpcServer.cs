namespace MigrationTool.Service.Core;

public interface IIpcServer
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);

    event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived;

    Task SendMessageAsync(string clientId, IpcMessage message, CancellationToken cancellationToken);
    Task BroadcastMessageAsync(IpcMessage message, CancellationToken cancellationToken);
}

public class IpcMessageReceivedEventArgs : EventArgs
{
    public string ClientId { get; }
    public IpcMessage Message { get; }

    public IpcMessageReceivedEventArgs(string clientId, IpcMessage message)
    {
        ClientId = clientId;
        Message = message;
    }
}

public class IpcMessage
{
    public string Type { get; set; } = string.Empty;
    public string? Payload { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }
}