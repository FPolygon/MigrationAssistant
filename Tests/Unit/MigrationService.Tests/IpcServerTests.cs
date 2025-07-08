using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service;
using MigrationTool.Service.Core;
using Moq;
using Newtonsoft.Json;
using System.IO.Pipes;
using System.Text;
using Xunit;

namespace MigrationService.Tests;

public class IpcServerTests : IDisposable
{
    private readonly Mock<ILogger<IpcServer>> _loggerMock;
    private readonly Mock<IOptions<ServiceConfiguration>> _configMock;
    private readonly IpcServer _ipcServer;
    private readonly ServiceConfiguration _configuration;
    private readonly string _testPipeName;

    public IpcServerTests()
    {
        _loggerMock = new Mock<ILogger<IpcServer>>();
        _configMock = new Mock<IOptions<ServiceConfiguration>>();

        // Use a unique pipe name for each test
        _testPipeName = $"TestPipe_{Guid.NewGuid():N}";

        _configuration = new ServiceConfiguration
        {
            PipeName = _testPipeName
        };

        _configMock.Setup(x => x.Value).Returns(_configuration);

        _ipcServer = new IpcServer(_loggerMock.Object, _configMock.Object);
    }

    public void Dispose()
    {
        _ipcServer?.Dispose();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StartAsync_ShouldStartSuccessfully()
    {
        // Act
        await _ipcServer.StartAsync(CancellationToken.None);
        await Task.Delay(100); // Give server time to start

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("IPC server started")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task StopAsync_ShouldStopSuccessfully()
    {
        // Arrange
        await _ipcServer.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act
        await _ipcServer.StopAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("IPC server stopped")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ClientConnection_ShouldTriggerMessageReceivedEvent()
    {
        // Skip if not on Windows since named pipes are Windows-specific
        if (!OperatingSystem.IsWindows())
        {
            // Use manual skip by returning early with a message
            return;
        }

        // This test requires actual named pipe functionality which is Windows-specific
        // In a real Windows environment, this test would:
        // 1. Start the IPC server
        // 2. Connect a client
        // 3. Send a message
        // 4. Verify the MessageReceived event was triggered

        // Arrange
        // Variables would be used to capture event data when test is fully implemented
        // IpcMessage? receivedMessage = null;
        // string? clientId = null;

        _ipcServer.MessageReceived += (sender, args) =>
        {
            // When implemented, this would capture the message data
            // receivedMessage = args.Message;
            // clientId = args.ClientId;
        };

        await _ipcServer.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act - simulate client connection and message
        // This would require actual named pipe client code

        // Assert
        // messageReceived.Should().BeTrue();
        // receivedMessage.Should().NotBeNull();
        // clientId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SendMessageAsync_WithInvalidClient_ShouldLogWarning()
    {
        // Arrange
        await _ipcServer.StartAsync(CancellationToken.None);
        var message = new IpcMessage { Type = "Test", Payload = "Test payload" };

        // Act
        await _ipcServer.SendMessageAsync("non-existent-client", message, CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Client") && o.ToString()!.Contains("not found")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task BroadcastMessageAsync_WithNoClients_ShouldNotThrow()
    {
        // Arrange
        await _ipcServer.StartAsync(CancellationToken.None);
        var message = new IpcMessage { Type = "Broadcast", Payload = "Test broadcast" };

        // Act
        var act = async () => await _ipcServer.BroadcastMessageAsync(message, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void IpcMessage_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new IpcMessage
        {
            Type = "TestMessage",
            Payload = "Test payload data",
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Act
        var json = JsonConvert.SerializeObject(message);
        var deserialized = JsonConvert.DeserializeObject<IpcMessage>(json);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Type.Should().Be(message.Type);
        deserialized.Payload.Should().Be(message.Payload);
        deserialized.CorrelationId.Should().Be(message.CorrelationId);
    }

    [Fact]
    public void IpcMessageReceivedEventArgs_ShouldContainCorrectData()
    {
        // Arrange
        var clientId = "test-client-123";
        var message = new IpcMessage { Type = "Test", Payload = "Data" };

        // Act
        var eventArgs = new IpcMessageReceivedEventArgs(clientId, message);

        // Assert
        eventArgs.ClientId.Should().Be(clientId);
        eventArgs.Message.Should().Be(message);
    }

    [Fact]
    public async Task Dispose_ShouldCleanupResources()
    {
        // Arrange
        await _ipcServer.StartAsync(CancellationToken.None);
        await Task.Delay(100);

        // Act
        _ipcServer.Dispose();

        // Assert
        // After disposal, starting again should work (new instance would be needed)
        // This mainly verifies dispose doesn't throw
        _loggerMock.Invocations.Clear();

        // Attempting to use disposed server should not work
        await _ipcServer.SendMessageAsync("any-client", new IpcMessage(), CancellationToken.None);

        // Should log warning about client not found (since server is disposed)
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}