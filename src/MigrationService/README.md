# Migration Service

The Migration Service is a Windows service that orchestrates user data migration to OneDrive before system reset for Autopilot enrollment. It runs with SYSTEM privileges to manage the migration process across all users on the machine.

**Current Status**: Phase 1 Complete - Core service infrastructure with IPC, state management, and logging is operational. User detection, backup providers, and agent communication features will be implemented in subsequent phases.

## Features

### Implemented (Phase 1)
- **State management**: SQLite database for persistent migration state with schema migrations
- **IPC communication**: Named pipes for communication with user agents
- **Automatic recovery**: Configurable restart on failure
- **Structured logging**: File, Event Log, and console logging with rotation
- **Service lifecycle**: Proper Windows service installation and management
- **Configuration**: JSON-based configuration with environment overrides

### Planned (Future Phases)
- **Multi-user orchestration**: Manages migration for all active users on the machine
- **User profile detection**: Identify and classify user profiles
- **Health monitoring**: Built-in health checks and diagnostics
- **Backup coordination**: Orchestrate backup providers
- **IT escalation**: Automatic issue escalation

## Architecture

```
MigrationService.exe (SYSTEM)
├── ServiceManager (Core orchestration)
├── StateManager (SQLite persistence)
├── IpcServer (Named pipe server)
├── MigrationStateOrchestrator (State transitions)
├── Logging (Structured logging system)
└── Configuration (appsettings.json)
```

## Prerequisites

```
MigrationService.exe (SYSTEM)
   ServiceManager (Core orchestration)
   StateManager (SQLite persistence)
   IpcServer (Named pipe server)
   Configuration (appsettings.json)
```

## Prerequisites

### Platform Support
**This service requires Windows x86_64 (64-bit).** No other platforms or architectures are supported.

### System Requirements
- Windows 10/11 (x86_64 only)
- .NET 8.0 Runtime or SDK
- Administrative privileges for installation
- C:\ProgramData\MigrationAssistant directory will be created

## Building

### Using .NET CLI

```powershell
# Build the service
dotnet build MigrationService.csproj

# Publish for deployment (Windows x86_64 only)
dotnet publish MigrationService.csproj -c Release -r win-x64 --self-contained false
```

### Using Visual Studio

1. Open MigrationService.csproj in Visual Studio
2. Build � Build Solution (Ctrl+Shift+B)
3. Build � Publish MigrationService

## Installation

### Using PowerShell Scripts (Recommended)

```powershell
# Install the service
.\Scripts\Install-Service.ps1 -StartService

# Install from specific path
.\Scripts\Install-Service.ps1 -ServicePath "C:\Program Files\MigrationAssistant\MigrationService.exe" -StartService

# Uninstall the service
.\Scripts\Uninstall-Service.ps1

# Uninstall and remove all data
.\Scripts\Uninstall-Service.ps1 -RemoveData -Force
```

### Using Command Line

```powershell
# Install
MigrationService.exe install

# Uninstall
MigrationService.exe uninstall

# Start/Stop
MigrationService.exe start
MigrationService.exe stop

# Check status
MigrationService.exe status
```

## Management

### Service Management Script

```powershell
# Show detailed service status
.\Scripts\Manage-Service.ps1 -Action Status

# View recent logs
.\Scripts\Manage-Service.ps1 -Action Logs -LogLines 100

# Test service connectivity
.\Scripts\Manage-Service.ps1 -Action Test

# Restart the service
.\Scripts\Manage-Service.ps1 -Action Restart

# Export diagnostics for support
.\Scripts\Manage-Service.ps1 -Action Export
```

### Manual Service Control

```powershell
# Using PowerShell
Start-Service MigrationService
Stop-Service MigrationService
Restart-Service MigrationService
Get-Service MigrationService

# Using SC command
sc start MigrationService
sc stop MigrationService
sc query MigrationService
```

## Configuration

### appsettings.json

```json
{
  "ServiceConfiguration": {
    "DataPath": "C:\\ProgramData\\MigrationAssistant\\Data",
    "LogPath": "C:\\ProgramData\\MigrationAssistant\\Logs",
    "PipeName": "MigrationService_{ComputerName}",
    "StateCheckIntervalSeconds": 300,
    "EnableDebugLogging": false
  }
}
```

### Configuration Options

- **DataPath**: Location for SQLite database and temporary files
- **LogPath**: Directory for log files
- **PipeName**: Named pipe for IPC ({ComputerName} is replaced automatically)
- **StateCheckIntervalSeconds**: How often to check migration status (default: 5 minutes)
- **EnableDebugLogging**: Enable verbose debug logging

## Logging

### Log Locations

- **File Logs**: `C:\ProgramData\MigrationAssistant\Logs\service-YYYYMMDD.txt`
- **Event Log**: Windows Application Event Log (Source: MigrationService)
- **Console**: When running interactively

### Log Levels

- **Debug**: Detailed diagnostic information (when EnableDebugLogging=true)
- **Information**: Normal operational messages
- **Warning**: Issues that don't prevent operation
- **Error**: Errors that may affect functionality
- **Critical**: Fatal errors requiring immediate attention

## Database

### SQLite Database

Location: `C:\ProgramData\MigrationAssistant\Data\migration.db`

Tables (Phase 1 Implementation):
- **SchemaVersions**: Database migration tracking
- **UserProfiles**: Detected user profiles and their status
- **MigrationStates**: Current migration state for each user
- **BackupOperations**: History of backup operations
- **OneDriveSync**: OneDrive status per user
- **ITEscalations**: IT escalation tracking
- **StateHistory**: Audit trail of state changes
- **DelayRequests**: User delay request tracking
- **ProviderResults**: Detailed results per backup provider
- **SystemEvents**: Service events and errors

### Database Management

```powershell
# View database (requires SQLite tools)
sqlite3 "C:\ProgramData\MigrationAssistant\Data\migration.db" ".tables"
sqlite3 "C:\ProgramData\MigrationAssistant\Data\migration.db" "SELECT * FROM UserProfiles;"
```

## IPC Communication

### Named Pipe

- **Pipe Name**: `\\.\pipe\MigrationService_{ComputerName}`
- **Protocol**: JSON messages over named pipe
- **Direction**: Bidirectional (service � agents)

### Message Format

```json
{
  "id": "unique-message-id",
  "type": "MESSAGE_TYPE",
  "timestamp": "2024-01-15T10:30:00Z",
  "payload": { }
}
```

See [API Design](../../docs/API_DESIGN.md) for detailed message types and payloads.

## Troubleshooting

### Service Won't Start

1. Check Event Log for errors:
   ```powershell
   Get-EventLog -LogName Application -Source MigrationService -Newest 10
   ```

2. Verify directories exist:
   ```powershell
   Test-Path "C:\ProgramData\MigrationAssistant"
   ```

3. Check service account permissions

4. Run connectivity test:
   ```powershell
   .\Scripts\Manage-Service.ps1 -Action Test
   ```

### High Memory Usage

1. Check database size:
   ```powershell
   Get-Item "C:\ProgramData\MigrationAssistant\Data\migration.db"
   ```

2. Review log file accumulation

3. Check for stale operations in database

### IPC Connection Issues

1. Verify pipe name matches between service and agents
2. Check firewall/antivirus blocking
3. Ensure service is running
4. Review IPC-related errors in logs

## Security

### Service Account

- Runs as: LOCAL SYSTEM (required for user profile access)
- Cannot be changed to a lower-privilege account

### Directory Permissions

- **C:\ProgramData\MigrationAssistant**:
  - SYSTEM: Full Control
  - Administrators: Full Control
  - Users: Read & Execute (for agent communication)

### Named Pipe Security

- SYSTEM: Full Control
- Administrators: Full Control
- Authenticated Users: Read/Write

## Development

### Running in Debug Mode

```powershell
# Set environment variable
$env:DOTNET_ENVIRONMENT = "Development"

# Run directly (not as service)
dotnet run --project MigrationService.csproj
```

### Unit Tests

```powershell
# Run all tests
dotnet test ..\..\Tests\Unit\MigrationService.Tests\MigrationService.Tests.csproj

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Adding New Features

1. Implement interfaces in Core folder
2. Register services in Program.cs
3. Add configuration to ServiceConfiguration
4. Update appsettings.json
5. Add unit tests
6. Update documentation

## Performance

### Resource Usage

- **Memory**: ~50-100 MB typical
- **CPU**: <1% idle, spikes during operations
- **Disk**: Database grows with user count
- **Network**: Minimal (IPC only)

### Optimization

- State checks run every 5 minutes (configurable)
- Database operations are async
- Automatic cleanup of old logs (30 days)
- Connection pooling for database

## Support

### Collecting Diagnostics

```powershell
# Export full diagnostics
.\Scripts\Manage-Service.ps1 -Action Export
```

This creates a ZIP file with:
- Service status
- Recent logs
- Event log entries
- System information

### Common Issues

1. **"Access Denied"**: Run PowerShell as Administrator
2. **"Service already exists"**: Uninstall first
3. **"Database locked"**: Stop service before manual database access
4. **"Pipe busy"**: Previous instance still running

## Version History

- **1.0.0**: Initial release with core functionality