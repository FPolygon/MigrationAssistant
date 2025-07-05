using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace MigrationTool.Service.Core;

[SupportedOSPlatform("windows")]
public class IpcServer : IIpcServer, IDisposable
{
    private readonly ILogger<IpcServer> _logger;
    private readonly ServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<string, ClientConnection> _clients;
    private CancellationTokenSource? _serverCancellation;
    private Task? _serverTask;
    private string _pipeName;

    public event EventHandler<IpcMessageReceivedEventArgs>? MessageReceived;

    public IpcServer(
        ILogger<IpcServer> logger,
        IOptions<ServiceConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;
        _clients = new ConcurrentDictionary<string, ClientConnection>();
        
        // Replace {ComputerName} placeholder with actual computer name
        _pipeName = _configuration.PipeName.Replace("{ComputerName}", Environment.MachineName);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting IPC server on pipe: {PipeName}", _pipeName);

        _serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _serverTask = Task.Run(() => RunServerAsync(_serverCancellation.Token), cancellationToken);

        await Task.CompletedTask;
        
        _logger.LogInformation("IPC server started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping IPC server");

        _serverCancellation?.Cancel();

        // Close all client connections
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();

        if (_serverTask != null)
        {
            try
            {
                await _serverTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("IPC server did not stop gracefully within timeout");
            }
        }

        _logger.LogInformation("IPC server stopped");
    }

    public async Task SendMessageAsync(string clientId, IpcMessage message, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            try
            {
                await client.SendMessageAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message to client {ClientId}", clientId);
                RemoveClient(clientId);
            }
        }
        else
        {
            _logger.LogWarning("Client {ClientId} not found", clientId);
        }
    }

    public async Task BroadcastMessageAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        var tasks = _clients.Values.Select(client => 
            SendMessageToClientSafelyAsync(client, message, cancellationToken));
        
        await Task.WhenAll(tasks);
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pipeSecurity = CreatePipeSecurity();
                
                using var serverPipe = NamedPipeServerStreamAcl.Create(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    pipeSecurity);

                _logger.LogDebug("Waiting for client connection");
                
                await serverPipe.WaitForConnectionAsync(cancellationToken);
                
                _logger.LogDebug("Client connected");

                // Handle client connection in a separate task
                _ = Task.Run(() => HandleClientAsync(serverPipe, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in IPC server loop");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream serverPipe, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString();
        var client = new ClientConnection(clientId, serverPipe, _logger);

        try
        {
            _clients[clientId] = client;
            _logger.LogInformation("Client {ClientId} connected", clientId);

            // Read messages from client
            while (serverPipe.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var message = await client.ReadMessageAsync(cancellationToken);
                if (message != null)
                {
                    _logger.LogDebug("Received message from {ClientId}: {Type}", clientId, message.Type);
                    MessageReceived?.Invoke(this, new IpcMessageReceivedEventArgs(clientId, message));
                }
                else
                {
                    // Null message means client disconnected
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client {ClientId}", clientId);
        }
        finally
        {
            RemoveClient(clientId);
            client.Dispose();
            _logger.LogInformation("Client {ClientId} disconnected", clientId);
        }
    }

    private void RemoveClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            client.Dispose();
        }
    }

    private async Task SendMessageToClientSafelyAsync(ClientConnection client, IpcMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await client.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message to client {ClientId}", client.Id);
            RemoveClient(client.Id);
        }
    }

    private PipeSecurity CreatePipeSecurity()
    {
        var pipeSecurity = new PipeSecurity();

        // Allow SYSTEM full control
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            systemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Allow Administrators full control
        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            adminSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Allow authenticated users to read/write
        var authenticatedSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            authenticatedSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return pipeSecurity;
    }

    public void Dispose()
    {
        _serverCancellation?.Cancel();
        _serverCancellation?.Dispose();
        
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
        _clients.Clear();
    }

    private class ClientConnection : IDisposable
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _writeLock;

        public string Id { get; }

        public ClientConnection(string id, NamedPipeServerStream pipe, ILogger logger)
        {
            Id = id;
            _pipe = pipe;
            _logger = logger;
            _reader = new StreamReader(pipe, Encoding.UTF8);
            _writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
            _writeLock = new SemaphoreSlim(1, 1);
        }

        public async Task<IpcMessage?> ReadMessageAsync(CancellationToken cancellationToken)
        {
            try
            {
                var json = await _reader.ReadLineAsync();
                if (string.IsNullOrEmpty(json))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<IpcMessage>(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read message from client {ClientId}", Id);
                return null;
            }
        }

        public async Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var json = JsonConvert.SerializeObject(message);
                await _writer.WriteLineAsync(json);
                await _writer.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Dispose()
        {
            _writeLock?.Dispose();
            _writer?.Dispose();
            _reader?.Dispose();
            _pipe?.Dispose();
        }
    }
}