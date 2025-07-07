using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.IPC;
using MigrationTool.Service.IPC.Messages;
using Xunit;

namespace MigrationService.Tests.IPC;

// NOTE: These integration tests use Windows named pipes which may not work properly in CI environments like GitHub Actions.
// Named pipes can fail due to:
// - Permission restrictions in containerized/sandboxed environments
// - Different security contexts between the test runner and named pipe server
// - Resource cleanup issues when tests run in parallel
// These tests are marked with [Trait("Category", "Integration")] to allow them to be filtered out in CI.

[Trait("Category", "Integration")]
[Trait("Category", "RequiresNamedPipes")]
public class IpcIntegrationTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;
    private readonly IIpcServer _server;
    private readonly string _pipeName;

    public IpcIntegrationTests()
    {
        _pipeName = $"TestPipe_{Guid.NewGuid()}";

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddIpcMessageHandling();
        services.AddSingleton<IIpcServer>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<IpcServer>>();
            var serializer = sp.GetRequiredService<IMessageSerializer>();
            var connectionManager = sp.GetRequiredService<IConnectionManager>();
            return new IpcServer(logger, serializer, connectionManager, _pipeName);
        });

        _serviceProvider = services.BuildServiceProvider();
        _server = _serviceProvider.GetRequiredService<IIpcServer>();
    }

    public async Task InitializeAsync()
    {
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _serviceProvider.Dispose();
    }

    [Fact]
    public async Task ClientServer_BasicCommunication_ShouldWork()
    {
        // Arrange
        var clientLogger = _serviceProvider.GetRequiredService<ILogger<IpcClient>>();
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
        var client = new IpcClient(clientLogger, serializer, _pipeName);

        var serverReceivedMessage = new TaskCompletionSource<IpcMessage>();
        var clientReceivedMessage = new TaskCompletionSource<IpcMessage>();

        _server.MessageReceived += (sender, args) =>
        {
            serverReceivedMessage.TrySetResult(args.Message);
        };

        client.MessageReceived += (sender, args) =>
        {
            clientReceivedMessage.TrySetResult(args.Message);
        };

        // Act
        await client.ConnectAsync();

        // Send message from client to server
        var clientMessage = MessageFactory.CreateAgentStarted("user-123", "1.0.0", "session-456");
        await client.SendMessageAsync(clientMessage);

        // Wait for server to receive message
        var receivedByServer = await serverReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Send message from server to client
        var serverMessage = MessageFactory.CreateBackupRequest("user-123", "normal", DateTime.UtcNow.AddDays(1), "files");
        await _server.SendMessageAsync(_server.ConnectedClients.ToString(), serverMessage);

        // Assert
        receivedByServer.Should().NotBeNull();
        receivedByServer.Type.Should().Be(MessageTypes.AgentStarted);

        var payload = serializer.DeserializePayload<AgentStartedPayload>(receivedByServer.Payload);
        payload!.UserId.Should().Be("user-123");

        await client.DisconnectAsync();
    }

    [Fact]
    public async Task MultipleClients_CanConnectSimultaneously()
    {
        // Arrange
        var clientCount = 5;
        var clients = new IIpcClient[clientCount];
        var connectTasks = new Task[clientCount];
        var connectedClients = 0;

        _server.ClientConnected += (sender, args) =>
        {
            Interlocked.Increment(ref connectedClients);
        };

        // Act
        for (int i = 0; i < clientCount; i++)
        {
            var clientLogger = _serviceProvider.GetRequiredService<ILogger<IpcClient>>();
            var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
            clients[i] = new IpcClient(clientLogger, serializer, _pipeName);
            connectTasks[i] = clients[i].ConnectAsync();
        }

        await Task.WhenAll(connectTasks);

        // Give server time to process connections
        await Task.Delay(100);

        // Assert
        connectedClients.Should().Be(clientCount);
        _server.ConnectedClients.Should().Be(clientCount);

        // Cleanup
        var disconnectTasks = new Task[clientCount];
        for (int i = 0; i < clientCount; i++)
        {
            disconnectTasks[i] = clients[i].DisconnectAsync();
            clients[i].Dispose();
        }
        await Task.WhenAll(disconnectTasks);
    }

    [Fact]
    public async Task Server_BroadcastMessage_ShouldReachAllClients()
    {
        // Arrange
        var client1Logger = _serviceProvider.GetRequiredService<ILogger<IpcClient>>();
        var client2Logger = _serviceProvider.GetRequiredService<ILogger<IpcClient>>();
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();

        var client1 = new IpcClient(client1Logger, serializer, _pipeName);
        var client2 = new IpcClient(client2Logger, serializer, _pipeName);

        var client1Received = new TaskCompletionSource<IpcMessage>();
        var client2Received = new TaskCompletionSource<IpcMessage>();

        client1.MessageReceived += (sender, args) => client1Received.TrySetResult(args.Message);
        client2.MessageReceived += (sender, args) => client2Received.TrySetResult(args.Message);

        await client1.ConnectAsync();
        await client2.ConnectAsync();

        // Give server time to register connections
        await Task.Delay(100);

        // Act
        var broadcastMessage = MessageFactory.CreateStatusUpdate("ready", new(), new(), 2);
        await _server.BroadcastMessageAsync(broadcastMessage);

        // Assert
        var message1 = await client1Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var message2 = await client2Received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        message1.Type.Should().Be(MessageTypes.StatusUpdate);
        message2.Type.Should().Be(MessageTypes.StatusUpdate);

        // Cleanup
        await client1.DisconnectAsync();
        await client2.DisconnectAsync();
        client1.Dispose();
        client2.Dispose();
    }

    [Fact]
    public async Task ReconnectingClient_ShouldReconnectAfterDisconnection()
    {
        // Arrange
        var clientLogger = _serviceProvider.GetRequiredService<ILogger<ReconnectingIpcClient>>();
        var innerClientLogger = _serviceProvider.GetRequiredService<ILogger<IpcClient>>();
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();

        var innerClient = new IpcClient(innerClientLogger, serializer, _pipeName);
        var reconnectingClient = new ReconnectingIpcClient(clientLogger, innerClient)
        {
            ReconnectDelay = TimeSpan.FromMilliseconds(100),
            MaxReconnectAttempts = 5
        };

        var connectionStateChanges = 0;
        reconnectingClient.ConnectionStateChanged += (sender, args) =>
        {
            Interlocked.Increment(ref connectionStateChanges);
        };

        // Act
        await reconnectingClient.ConnectAsync();
        reconnectingClient.IsConnected.Should().BeTrue();

        // Simulate server restart
        await _server.StopAsync();
        await Task.Delay(200); // Wait for client to detect disconnection

        reconnectingClient.IsConnected.Should().BeFalse();
        reconnectingClient.IsReconnecting.Should().BeTrue();

        // Restart server
        await _server.StartAsync();

        // Wait for reconnection
        var maxWait = TimeSpan.FromSeconds(5);
        var start = DateTime.UtcNow;
        while (!reconnectingClient.IsConnected && DateTime.UtcNow - start < maxWait)
        {
            await Task.Delay(100);
        }

        // Assert
        reconnectingClient.IsConnected.Should().BeTrue();
        connectionStateChanges.Should().BeGreaterThan(2); // Disconnected and reconnected

        // Cleanup
        await reconnectingClient.DisconnectAsync();
        reconnectingClient.Dispose();
    }

    [Fact]
    public async Task MessageWithLargePayload_ShouldBeTransmittedCorrectly()
    {
        // Arrange
        var clientLogger = _serviceProvider.GetRequiredService<ILogger<IpcClient>>();
        var serializer = _serviceProvider.GetRequiredService<IMessageSerializer>();
        var client = new IpcClient(clientLogger, serializer, _pipeName);

        var serverReceivedMessage = new TaskCompletionSource<IpcMessage>();
        _server.MessageReceived += (sender, args) =>
        {
            serverReceivedMessage.TrySetResult(args.Message);
        };

        await client.ConnectAsync();

        // Create a large payload
        var largeText = new string('X', 100_000); // 100KB of text
        var message = MessageFactory.CreateErrorReport(
            "user-123",
            "TEST_ERROR",
            largeText,
            null,
            null);

        // Act
        await client.SendMessageAsync(message);

        // Assert
        var received = await serverReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
        received.Should().NotBeNull();
        received.Type.Should().Be(MessageTypes.ErrorReport);

        var payload = serializer.DeserializePayload<ErrorReportPayload>(received.Payload);
        payload!.Message.Should().Be(largeText);

        // Cleanup
        await client.DisconnectAsync();
        client.Dispose();
    }
}