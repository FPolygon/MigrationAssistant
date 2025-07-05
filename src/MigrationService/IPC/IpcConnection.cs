using System;
using System.Buffers;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.IPC;

public class IpcConnection : IIpcConnection
{
    private readonly ILogger<IpcConnection> _logger;
    private readonly IMessageSerializer _serializer;
    private readonly NamedPipeServerStream _pipeStream;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private readonly SemaphoreSlim _readSemaphore = new(1, 1);
    
    private CancellationTokenSource? _connectionCts;
    private Task? _readTask;
    private bool _disposed;
    
    public string ClientId { get; }
    public bool IsConnected => _pipeStream.IsConnected && !_disposed;
    public DateTime ConnectedAt { get; }
    public DateTime? LastMessageAt { get; private set; }
    public string? UserId { get; private set; }
    
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ClientDisconnectedEventArgs>? Disconnected;
    
    public IpcConnection(
        string clientId,
        NamedPipeServerStream pipeStream,
        ILogger<IpcConnection> logger,
        IMessageSerializer serializer)
    {
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _pipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        ConnectedAt = DateTime.UtcNow;
    }
    
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(IpcConnection));
        }
        
        _connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readTask = ReadMessagesAsync(_connectionCts.Token);
        
        _logger.LogDebug("Started connection {ClientId}", ClientId);
        
        await Task.CompletedTask;
    }
    
    public async Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException($"Client {ClientId} is not connected");
        }
        
        await _sendSemaphore.WaitAsync(cancellationToken);
        try
        {
            var messageBytes = _serializer.SerializeMessage(message);
            var lengthPrefix = BitConverter.GetBytes(messageBytes.Length);
            
            // Write length prefix
            await _pipeStream.WriteAsync(lengthPrefix, 0, lengthPrefix.Length, cancellationToken);
            
            // Write message
            await _pipeStream.WriteAsync(messageBytes, 0, messageBytes.Length, cancellationToken);
            await _pipeStream.FlushAsync(cancellationToken);
            
            _logger.LogDebug("Sent message {MessageType} to client {ClientId}", message.Type, ClientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to client {ClientId}", ClientId);
            await DisconnectAsync($"Send error: {ex.Message}");
            throw;
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }
    
    public async Task DisconnectAsync(string? reason = null)
    {
        if (_disposed)
        {
            return;
        }
        
        _logger.LogInformation("Disconnecting client {ClientId}: {Reason}", ClientId, reason ?? "Requested");
        
        try
        {
            _connectionCts?.Cancel();
            
            if (_readTask != null)
            {
                try
                {
                    await _readTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Timeout waiting for read task to complete for client {ClientId}", ClientId);
                }
            }
            
            if (_pipeStream.IsConnected)
            {
                _pipeStream.Disconnect();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting client {ClientId}", ClientId);
        }
        finally
        {
            Disconnected?.Invoke(this, new ClientDisconnectedEventArgs(ClientId, DateTime.UtcNow, reason));
        }
    }
    
    public void SetUserId(string userId)
    {
        UserId = userId;
        _logger.LogInformation("Set UserId {UserId} for client {ClientId}", userId, ClientId);
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
                    await _readSemaphore.WaitAsync(cancellationToken);
                    try
                    {
                        // Read length prefix (4 bytes)
                        var lengthBuffer = new byte[4];
                        var bytesRead = await ReadExactAsync(_pipeStream, lengthBuffer, 0, 4, cancellationToken);
                        
                        if (bytesRead < 4)
                        {
                            _logger.LogWarning("Failed to read message length from client {ClientId}", ClientId);
                            break;
                        }
                        
                        var messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                        
                        if (messageLength <= 0 || messageLength > 1024 * 1024) // Max 1MB
                        {
                            _logger.LogWarning("Invalid message length {Length} from client {ClientId}", messageLength, ClientId);
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
                        
                        bytesRead = await ReadExactAsync(_pipeStream, messageBuffer, 0, messageLength, cancellationToken);
                        
                        if (bytesRead < messageLength)
                        {
                            _logger.LogWarning("Failed to read complete message from client {ClientId}", ClientId);
                            break;
                        }
                        
                        // Deserialize and handle message
                        var messageBytes = messageLength <= buffer.Length 
                            ? messageBuffer.AsSpan(0, messageLength).ToArray()
                            : messageBuffer;
                            
                        var message = _serializer.DeserializeMessage(messageBytes);
                        
                        if (message != null)
                        {
                            LastMessageAt = DateTime.UtcNow;
                            _logger.LogDebug("Received message {MessageType} from client {ClientId}", message.Type, ClientId);
                            
                            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(ClientId, message));
                        }
                    }
                    finally
                    {
                        _readSemaphore.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (EndOfStreamException)
                {
                    _logger.LogInformation("Client {ClientId} disconnected", ClientId);
                    break;
                }
                catch (IOException ioEx)
                {
                    _logger.LogWarning(ioEx, "IO error reading from client {ClientId}", ClientId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading message from client {ClientId}", ClientId);
                    break;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            await DisconnectAsync("Read loop ended");
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
    
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;
        
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        
        try
        {
            if (_pipeStream.IsConnected)
            {
                _pipeStream.Disconnect();
            }
            _pipeStream.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing pipe stream for client {ClientId}", ClientId);
        }
        
        _sendSemaphore.Dispose();
        _readSemaphore.Dispose();
    }
}