using System;
using System.Buffers;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.IPC;

public interface IIpcClient : IDisposable
{
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    
    bool IsConnected { get; }
    string ClientId { get; }
    
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default);
}

public class IpcClient : IIpcClient
{
    private readonly ILogger<IpcClient> _logger;
    private readonly IMessageSerializer _serializer;
    private readonly string _pipeName;
    private readonly string _clientId;
    private readonly SemaphoreSlim _connectSemaphore = new(1, 1);
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    
    private NamedPipeClientStream? _pipeClient;
    private CancellationTokenSource? _connectionCts;
    private Task? _readTask;
    private bool _disposed;
    
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    
    public bool IsConnected => _pipeClient?.IsConnected ?? false;
    public string ClientId => _clientId;
    
    public IpcClient(
        ILogger<IpcClient> logger,
        IMessageSerializer serializer,
        string? pipeName = null,
        string? clientId = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _pipeName = pipeName ?? $"MigrationService_{Environment.MachineName}";
        _clientId = clientId ?? Guid.NewGuid().ToString();
    }
    
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _connectSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                _logger.LogWarning("Client is already connected");
                return;
            }
            
            _logger.LogInformation("Connecting to pipe: {PipeName}", _pipeName);
            
            _pipeClient = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.WriteThrough);
            
            await _pipeClient.ConnectAsync(cancellationToken);
            _pipeClient.ReadMode = PipeTransmissionMode.Message;
            
            _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readTask = ReadMessagesAsync(_connectionCts.Token);
            
            _logger.LogInformation("Connected to IPC server");
            
            OnConnectionStateChanged(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to IPC server");
            
            _pipeClient?.Dispose();
            _pipeClient = null;
            
            throw;
        }
        finally
        {
            _connectSemaphore.Release();
        }
    }
    
    public async Task DisconnectAsync()
    {
        await _connectSemaphore.WaitAsync();
        try
        {
            if (!IsConnected)
            {
                return;
            }
            
            _logger.LogInformation("Disconnecting from IPC server");
            
            _connectionCts?.Cancel();
            
            if (_readTask != null)
            {
                try
                {
                    await _readTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Timeout waiting for read task to complete");
                }
            }
            
            _pipeClient?.Dispose();
            _pipeClient = null;
            
            OnConnectionStateChanged(false);
        }
        finally
        {
            _connectSemaphore.Release();
        }
    }
    
    public async Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Client is not connected");
        }
        
        await _sendSemaphore.WaitAsync(cancellationToken);
        try
        {
            var messageBytes = _serializer.SerializeMessage(message);
            var lengthPrefix = BitConverter.GetBytes(messageBytes.Length);
            
            // Write length prefix
            await _pipeClient!.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, cancellationToken);
            
            // Write message
            await _pipeClient.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
            await _pipeClient.FlushAsync(cancellationToken);
            
            _logger.LogDebug("Sent message {MessageType}", message.Type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            await DisconnectAsync();
            throw;
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
    
    private async Task ReadMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected)
            {
                try
                {
                    // Read length prefix (4 bytes)
                    var lengthBuffer = new byte[4];
                    var bytesRead = await ReadExactAsync(_pipeClient!, lengthBuffer, 0, 4, cancellationToken);
                    
                    if (bytesRead < 4)
                    {
                        _logger.LogWarning("Failed to read message length");
                        break;
                    }
                    
                    var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                    
                    if (messageLength <= 0 || messageLength > 1024 * 1024) // Max 1MB
                    {
                        _logger.LogWarning("Invalid message length: {Length}", messageLength);
                        break;
                    }
                    
                    // Read message
                    byte[] messageBuffer;
                    if (messageLength <= buffer.Length)
                    {
                        messageBuffer = buffer;
                    }
                    else
                    {
                        messageBuffer = new byte[messageLength];
                    }
                    
                    bytesRead = await ReadExactAsync(_pipeClient!, messageBuffer, 0, messageLength, cancellationToken);
                    
                    if (bytesRead < messageLength)
                    {
                        _logger.LogWarning("Failed to read complete message");
                        break;
                    }
                    
                    // Deserialize and handle message
                    var messageBytes = messageLength <= buffer.Length 
                        ? messageBuffer.AsSpan(0, messageLength).ToArray()
                        : messageBuffer;
                        
                    var message = _serializer.DeserializeMessage(messageBytes);
                    
                    if (message != null)
                    {
                        _logger.LogDebug("Received message {MessageType}", message.Type);
                        MessageReceived?.Invoke(this, new MessageReceivedEventArgs(_clientId, message));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (EndOfStreamException)
                {
                    _logger.LogInformation("Server disconnected");
                    break;
                }
                catch (IOException ioEx)
                {
                    _logger.LogWarning(ioEx, "IO error reading from server");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading message");
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await DisconnectAsync();
        }
    }
    
    private async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken);
            
            if (read == 0)
            {
                break;
            }
            
            totalRead += read;
        }
        
        return totalRead;
    }
    
    private void OnConnectionStateChanged(bool connected)
    {
        ConnectionStateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(connected));
    }
    
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;
        
        DisconnectAsync().GetAwaiter().GetResult();
        
        _connectSemaphore.Dispose();
        _sendSemaphore.Dispose();
    }
}

public class ConnectionStateChangedEventArgs : EventArgs
{
    public bool IsConnected { get; }
    
    public ConnectionStateChangedEventArgs(bool isConnected)
    {
        IsConnected = isConnected;
    }
}