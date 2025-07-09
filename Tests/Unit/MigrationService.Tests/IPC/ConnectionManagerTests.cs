using System;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.IPC;
using MigrationTool.Service.IPC.Messages;
using Moq;
using Xunit;

namespace MigrationService.Tests.IPC;

public class ConnectionManagerTests : IDisposable
{
    private readonly Mock<ILogger<ConnectionManager>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly Mock<IMessageSerializer> _serializerMock;
    private readonly ConnectionManager _connectionManager;

    public ConnectionManagerTests()
    {
        _loggerMock = new Mock<ILogger<ConnectionManager>>();
        _loggerFactoryMock = new Mock<ILoggerFactory>();
        _serializerMock = new Mock<IMessageSerializer>();

        // Setup logger factory to return mock loggers
        _loggerFactoryMock.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());

        _connectionManager = new ConnectionManager(
            _loggerMock.Object,
            _loggerFactoryMock.Object,
            _serializerMock.Object);
    }

    public void Dispose()
    {
        _connectionManager.Dispose();
    }

    [Fact]
    public async Task AddConnectionAsync_ShouldAddConnectionSuccessfully()
    {
        // Arrange
        var clientId = "test-client-1";
        using var pipeStream = CreateMockPipeStream();

        // Act
        var connection = await _connectionManager.AddConnectionAsync(clientId, pipeStream);

        // Assert
        connection.Should().NotBeNull();
        connection.ClientId.Should().Be(clientId);
        _connectionManager.ActiveConnectionCount.Should().Be(1);
        _connectionManager.ActiveClientIds.Should().Contain(clientId);
    }

    [Fact]
    public async Task AddConnectionAsync_DuplicateClientId_ShouldThrowException()
    {
        // Arrange
        var clientId = "duplicate-client";
        using var pipeStream1 = CreateMockPipeStream();
        using var pipeStream2 = CreateMockPipeStream();

        await _connectionManager.AddConnectionAsync(clientId, pipeStream1);

        // Act & Assert
        var act = () => _connectionManager.AddConnectionAsync(clientId, pipeStream2);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Client {clientId} is already connected");
    }

    [Fact]
    public async Task RemoveConnectionAsync_ShouldRemoveConnection()
    {
        // Arrange
        var clientId = "test-client-2";
        using var pipeStream = CreateMockPipeStream();

        await _connectionManager.AddConnectionAsync(clientId, pipeStream);

        // Act
        await _connectionManager.RemoveConnectionAsync(clientId);

        // Assert
        _connectionManager.ActiveConnectionCount.Should().Be(0);
        _connectionManager.ActiveClientIds.Should().NotContain(clientId);
    }

    [Fact]
    public async Task IsConnectedAsync_WithConnectedClient_ShouldReturnTrue()
    {
        // Arrange
        var clientId = "test-client-3";
        var connectionMock = new Mock<IIpcConnection>();
        connectionMock.Setup(x => x.ClientId).Returns(clientId);
        connectionMock.Setup(x => x.IsConnected).Returns(true);

        // Use reflection to add the mock connection directly
        var connectionsField = _connectionManager.GetType()
            .GetField("_connections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var connections = connectionsField!.GetValue(_connectionManager) as System.Collections.Concurrent.ConcurrentDictionary<string, IIpcConnection>;
        connections!.TryAdd(clientId, connectionMock.Object);

        // Act
        var isConnected = await _connectionManager.IsConnectedAsync(clientId);

        // Assert
        isConnected.Should().BeTrue();
    }

    [Fact]
    public async Task IsConnectedAsync_WithUnknownClient_ShouldReturnFalse()
    {
        // Act
        var isConnected = await _connectionManager.IsConnectedAsync("unknown-client");

        // Assert
        isConnected.Should().BeFalse();
    }

    [Fact]
    public async Task SendMessageAsync_ToConnectedClient_ShouldSucceed()
    {
        // Arrange
        var clientId = "test-client-4";
        var connectionMock = new Mock<IIpcConnection>();
        connectionMock.Setup(x => x.ClientId).Returns(clientId);
        connectionMock.Setup(x => x.IsConnected).Returns(true);
        connectionMock.Setup(x => x.SendMessageAsync(It.IsAny<IpcMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Use reflection or a test-specific method to add the mock connection
        var connectionsField = _connectionManager.GetType()
            .GetField("_connections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var connections = connectionsField!.GetValue(_connectionManager) as System.Collections.Concurrent.ConcurrentDictionary<string, IIpcConnection>;
        connections!.TryAdd(clientId, connectionMock.Object);

        var message = MessageFactory.CreateHeartbeat("server", 1);

        // Act
        await _connectionManager.SendMessageAsync(clientId, message);

        // Assert
        connectionMock.Verify(x => x.SendMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendMessageAsync_ToUnknownClient_ShouldThrowException()
    {
        // Arrange
        var message = MessageFactory.CreateHeartbeat("server", 1);

        // Act & Assert
        var act = () => _connectionManager.SendMessageAsync("unknown-client", message);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Client unknown-client is not connected");
    }

    [Fact]
    public async Task BroadcastMessageAsync_ShouldSendToAllConnectedClients()
    {
        // Arrange
        var connections = new[]
        {
            CreateMockConnection("client1", true),
            CreateMockConnection("client2", true),
            CreateMockConnection("client3", false) // Disconnected
        };

        foreach (var (clientId, mock) in connections)
        {
            var connectionsField = _connectionManager.GetType()
                .GetField("_connections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var connectionDict = connectionsField!.GetValue(_connectionManager) as System.Collections.Concurrent.ConcurrentDictionary<string, IIpcConnection>;
            connectionDict!.TryAdd(clientId, mock.Object);
        }

        var message = MessageFactory.CreateStatusUpdate("ready", new(), new(), 3);

        // Act
        await _connectionManager.BroadcastMessageAsync(message);

        // Assert
        connections[0].mock.Verify(x => x.SendMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        connections[1].mock.Verify(x => x.SendMessageAsync(message, It.IsAny<CancellationToken>()), Times.Once);
        connections[2].mock.Verify(x => x.SendMessageAsync(message, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisconnectAllAsync_ShouldDisconnectAllClients()
    {
        // Arrange
        var connections = new[]
        {
            CreateMockConnection("client1", true),
            CreateMockConnection("client2", true)
        };

        foreach (var (clientId, mock) in connections)
        {
            var connectionsField = _connectionManager.GetType()
                .GetField("_connections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var connectionDict = connectionsField!.GetValue(_connectionManager) as System.Collections.Concurrent.ConcurrentDictionary<string, IIpcConnection>;
            connectionDict!.TryAdd(clientId, mock.Object);
        }

        // Act
        await _connectionManager.DisconnectAllAsync();

        // Assert
        _connectionManager.ActiveConnectionCount.Should().Be(0);
        connections[0].mock.Verify(x => x.DisconnectAsync("Server shutdown"), Times.Once);
        connections[1].mock.Verify(x => x.DisconnectAsync("Server shutdown"), Times.Once);
    }

    [Fact]
    public void GetConnection_WithExistingClient_ShouldReturnConnection()
    {
        // Arrange
        var clientId = "test-client-5";
        var connectionMock = new Mock<IIpcConnection>();
        connectionMock.Setup(x => x.ClientId).Returns(clientId);

        var connectionsField = _connectionManager.GetType()
            .GetField("_connections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var connections = connectionsField!.GetValue(_connectionManager) as System.Collections.Concurrent.ConcurrentDictionary<string, IIpcConnection>;
        connections!.TryAdd(clientId, connectionMock.Object);

        // Act
        var connection = _connectionManager.GetConnection(clientId);

        // Assert
        connection.Should().Be(connectionMock.Object);
    }

    [Fact]
    public void GetConnection_WithUnknownClient_ShouldReturnNull()
    {
        // Act
        var connection = _connectionManager.GetConnection("unknown-client");

        // Assert
        connection.Should().BeNull();
    }

    private NamedPipeServerStream CreateMockPipeStream()
    {
        // Create a real pipe stream for testing
        var pipeName = $"test_pipe_{Guid.NewGuid()}";
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous);
    }

    private (string clientId, Mock<IIpcConnection> mock) CreateMockConnection(string clientId, bool isConnected)
    {
        var mock = new Mock<IIpcConnection>();
        mock.Setup(x => x.ClientId).Returns(clientId);
        mock.Setup(x => x.IsConnected).Returns(isConnected);
        mock.Setup(x => x.SendMessageAsync(It.IsAny<IpcMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(x => x.DisconnectAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return (clientId, mock);
    }
}
