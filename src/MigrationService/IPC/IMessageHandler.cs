using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.IPC;

public interface IMessageHandler
{
    string MessageType { get; }
    Task<IpcMessage?> HandleAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default);
}

public interface IMessageHandler<TPayload> : IMessageHandler where TPayload : class
{
    Task<IpcMessage?> HandleAsync(string clientId, TPayload payload, CancellationToken cancellationToken = default);
}
