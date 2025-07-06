# IPC Framework

This directory contains the Inter-Process Communication (IPC) framework that enables bidirectional communication between the Migration Service (running as SYSTEM) and per-user Migration Agents using named pipes with a JSON message protocol.

## Architecture

### Core Components

1. **IpcServer** - Named pipe server that accepts multiple client connections
2. **IpcClient** - Client implementation for agents to connect to the service
3. **IpcConnection** - Manages individual client connections
4. **ConnectionManager** - Manages all active connections
5. **MessageSerializer** - JSON serialization/deserialization
6. **MessageDispatcher** - Routes messages to appropriate handlers
7. **ReconnectingIpcClient** - Adds automatic reconnection and message queuing

### Message Protocol

All messages follow this JSON structure:
```json
{
  "id": "unique-message-id",
  "type": "MESSAGE_TYPE",
  "timestamp": "2024-01-01T12:00:00Z",
  "payload": { }
}
```

### Message Types

#### Service → Agent
- `BACKUP_REQUEST` - Request user to start backup
- `STATUS_UPDATE` - Update on overall migration status
- `ESCALATION_NOTICE` - Notify about IT escalation
- `CONFIGURATION_UPDATE` - Push configuration changes
- `SHUTDOWN_REQUEST` - Request agent shutdown

#### Agent → Service
- `AGENT_STARTED` - Agent has started and is ready
- `BACKUP_STARTED` - User has initiated backup
- `BACKUP_PROGRESS` - Progress updates during backup
- `BACKUP_COMPLETED` - Backup has finished
- `DELAY_REQUEST` - User requests deadline extension
- `USER_ACTION` - Other user-initiated actions
- `ERROR_REPORT` - Report errors to service

#### Bidirectional
- `HEARTBEAT` - Keep-alive messages
- `ACKNOWLEDGMENT` - Message receipt confirmation

## Usage

### Server Setup

```csharp
// In your service startup
services.AddIpcMessageHandling();
services.AddMessageHandler<AgentStartedHandler>();
services.AddMessageHandler<BackupProgressHandler>();
// ... add other handlers

// Start the server
var server = serviceProvider.GetRequiredService<IIpcServer>();
await server.StartAsync();

// Handle messages
server.MessageReceived += (sender, args) =>
{
    // Messages are automatically dispatched to registered handlers
};
```

### Client Setup

```csharp
// Create client
var client = new ReconnectingIpcClient(logger, 
    new IpcClient(clientLogger, serializer));

// Connect
await client.ConnectAsync();

// Send messages
var message = MessageFactory.CreateAgentStarted(userId, version, sessionId);
await client.SendMessageAsync(message);

// Handle responses
client.MessageReceived += (sender, args) =>
{
    // Process server messages
};
```

### Creating Message Handlers

```csharp
public class MyHandler : MessageHandler<MyPayload>
{
    public override string MessageType => MessageTypes.MyMessageType;
    
    public override async Task<IpcMessage?> HandleAsync(
        string clientId, 
        MyPayload payload, 
        CancellationToken cancellationToken)
    {
        // Process the message
        // Return response or null for acknowledgment
    }
}
```

## Features

### Security
- Named pipes use Windows ACLs to restrict access
- Only authenticated users can connect
- SYSTEM account has full control

### Reliability
- Automatic reconnection on disconnect
- Message queuing when disconnected
- Heartbeat/keepalive mechanism
- Exponential backoff for reconnection

### Performance
- Async/await throughout
- Connection pooling
- Message batching support
- Binary message framing with length prefix

### Monitoring
- Comprehensive logging
- Connection state tracking
- Message acknowledgments
- Performance metrics

## Testing

The framework includes comprehensive tests:
- Unit tests for serialization, dispatching, connection management
- Integration tests for full client-server communication
- Multi-client stress tests
- Reconnection scenario tests

Run tests:
```bash
dotnet test --filter "FullyQualifiedName~MigrationService.Tests.IPC"
```

## Implementation Notes

1. **Message Size**: Maximum message size is 1MB to prevent memory issues
2. **Timeouts**: Default connection timeout is 30 seconds
3. **Heartbeat**: Sent every 30 seconds to detect stale connections
4. **Reconnection**: Max 10 attempts with exponential backoff
5. **Concurrency**: Server handles multiple clients concurrently

## Future Enhancements

1. Add encryption for sensitive data
2. Implement message compression for large payloads
3. Add metrics collection (messages/sec, latency, etc.)
4. Support for request-response correlation
5. Message priority queuing