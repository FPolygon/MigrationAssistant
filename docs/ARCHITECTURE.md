# Migration Tool Architecture

## System Overview

The Windows Migration Tool is designed as a distributed system with multiple components working together to ensure safe and complete data migration for all users on a shared computer.

## Core Design Principles

1. **Multi-User Safety**: Never reset until ALL active users have backed up
2. **User Autonomy**: Users control their own backup timing within limits
3. **Data Integrity**: Verify all backups before allowing reset
4. **Minimal Disruption**: Smart notifications that avoid interrupting work
5. **IT Oversight**: Automatic escalation for issues requiring intervention

## Component Architecture

### 1. MigrationService.exe (Windows Service) ✅ Phase 1 Complete

**Purpose**: Central orchestrator running with SYSTEM privileges

**Responsibilities**:
- User profile detection and classification (Phase 2)
- Migration state management ✅ Implemented
- Backup orchestration across all users (Phase 2)
- IT escalation triggers (Phase 8)
- Reset authorization logic (Phase 7)

**Key Classes** (Implemented in Phase 1):
- `ServiceManager`: Windows service lifecycle management ✅
- `MigrationWindowsService`: Service base implementation ✅
- `StateManager`: SQLite-based state management ✅
- `IpcServer`: Named pipe server for agent communication ✅
- `MigrationStateOrchestrator`: Core orchestration logic ✅

**Key Classes** (Planned for future phases):
- `UserProfileManager`: Profile detection and classification (Phase 2)
- `BackupOrchestrator`: Coordinates backup across users (Phase 5-6)
- `EscalationManager`: IT escalation logic (Phase 8)

**State Management** (Phase 1 Complete):
- SQLite database for persistent state ✅
- Database migration system ✅
- Structured logging system ✅

### 2. MigrationAgent.exe (User Agent) 📅 Phase 4

**Purpose**: Per-user process for notifications and interaction

**Responsibilities**:
- Display migration notifications
- Show backup progress
- Handle user delay requests
- Detect meeting/fullscreen states
- Communicate with service

**Key Classes**:
- `NotificationManager`: Smart notification logic
- `MeetingDetector`: Teams/Zoom/camera detection
- `IpcClient`: Named pipe client
- `NotificationWindow`: WPF sliding panel UI

**Smart Detection Features**:
- Process monitoring for meeting apps
- Camera/microphone usage via Windows APIs
- Fullscreen application detection
- Calendar integration via Graph API

### 3. MigrationBackup.dll (Backup Engine) 📅 Phase 5-6

**Purpose**: Modular backup system with provider pattern

**Architecture**:
```
BackupEngine
├── IBackupProvider (Interface)
├── FileBackupProvider
├── BrowserBackupProvider
├── EmailBackupProvider
└── SystemBackupProvider
```

**Provider Responsibilities**:

**FileBackupProvider**:
- Standard folder backup (Documents, Desktop, etc.)
- Non-standard location discovery
- AppData selection logic
- File filtering and deduplication

**BrowserBackupProvider**:
- Multi-browser support (Edge, Chrome, Firefox)
- Profile detection
- Bookmark/password export
- Extension inventory
- Sync account detection

**EmailBackupProvider**:
- Outlook version detection (2016, Classic, New)
- Profile enumeration
- Settings/signature export
- PST file handling
- Autocomplete cache backup

**SystemBackupProvider**:
- WiFi profile export
- Credential Manager backup
- Network drive mappings
- Printer configurations
- Desktop customizations

**OneDrive Integration**:
- `OneDriveManager`: API wrapper and sync control
- `QuotaChecker`: Space verification
- `SyncMonitor`: Progress tracking

### 4. MigrationRestore.exe (Restore Wizard) 📅 Phase 9

**Purpose**: Standalone post-reset recovery tool

**Design Constraints**:
- Self-contained executable (no dependencies)
- Must work on fresh Windows installation
- Clear category-based restoration
- Error recovery capabilities

**Key Features**:
- Manifest-driven restoration
- Priority-based recovery order
- Selective restoration options
- Progress visualization
- Error reporting

## Current Implementation Status (Phase 1 Complete)

### Implemented Components

1. **Core Service Infrastructure**:
   - Windows service with proper lifecycle management
   - Service installation and configuration scripts
   - Automatic recovery and restart capabilities

2. **IPC Framework**:
   - Named pipe server implementation
   - JSON message protocol with serialization
   - Message dispatcher and handler architecture
   - Connection management with heartbeat
   - Reconnecting client for resilience

3. **State Management**:
   - SQLite database integration
   - Database migration system
   - State transition validation
   - Thread-safe state operations

4. **Logging System**:
   - Structured logging with multiple providers
   - File rotation (size and time-based)
   - Event log integration
   - Dynamic configuration
   - JSON formatting support

5. **Deployment Tools**:
   - PowerShell deployment script
   - Service management scripts
   - Build automation

### Test Coverage
- Unit tests for core components
- Integration tests for IPC
- Current coverage: 53.2% (targeting 70%)

## Data Flow

### Backup Flow
```
1. Service detects user profiles
2. Service requests backup via agent
3. Agent shows notification to user
4. User initiates backup
5. Backup engine creates manifest
6. Providers backup data to staging
7. OneDrive manager uploads to cloud
8. Service tracks completion
9. All users complete = allow reset
```

### Communication Architecture

**Service ↔ Agent Communication**:
- Named pipes for IPC
- JSON message protocol
- Heartbeat for liveness
- Async command/response pattern

**Message Types**:
```json
{
  "type": "BACKUP_REQUEST",
  "userId": "user123",
  "priority": "normal",
  "deadline": "2024-01-15T10:00:00Z"
}
```

## Security Considerations

### Privilege Separation
- Service runs as SYSTEM (required for profile access)
- Agent runs as user (for OneDrive access)
- Backup operations use user token impersonation

### Data Protection
- Sensitive data encrypted in transit
- Backup manifests include checksums
- Credentials stored using DPAPI
- Audit logging for all operations

### Communication Security
- Named pipes with ACL restrictions
- Message signing for integrity
- No network communication (except OneDrive)

## State Management

### Service State (SQLite)
```sql
CREATE TABLE migration_state (
    computer_name TEXT PRIMARY KEY,
    migration_id TEXT,
    start_date DATETIME,
    deadline DATETIME,
    status TEXT
);

CREATE TABLE user_backups (
    user_sid TEXT PRIMARY KEY,
    username TEXT,
    profile_path TEXT,
    backup_status TEXT,
    backup_start DATETIME,
    backup_complete DATETIME,
    backup_size_mb INTEGER,
    errors TEXT
);

CREATE TABLE escalations (
    id INTEGER PRIMARY KEY,
    user_sid TEXT,
    reason TEXT,
    details TEXT,
    created_date DATETIME,
    resolved BOOLEAN
);
```

### Backup Manifest (JSON)
```json
{
  "version": "1.0",
  "computer": "DESKTOP-ABC123",
  "user": "john.doe",
  "backup_date": "2024-01-10T14:30:00Z",
  "categories": {
    "files": {
      "items": 1523,
      "size_mb": 2048,
      "checksum": "sha256:..."
    },
    "browsers": {
      "edge": { "profiles": 1, "bookmarks": 245 },
      "chrome": { "profiles": 2, "bookmarks": 523 }
    },
    "email": {
      "profiles": ["Outlook", "Personal"],
      "pst_files": 2,
      "total_size_mb": 4096
    }
  }
}
```

## Error Handling Strategy

### Retry Logic
- Exponential backoff for transient failures
- Maximum retry limits per operation
- Different strategies per provider

### Graceful Degradation
- Continue backup even if one provider fails
- Mark partial backups clearly
- Allow manual intervention

### IT Escalation Triggers
1. OneDrive quota insufficient
2. Sync errors >30 minutes
3. Large PST files (>5GB)
4. Backup failures after retries
5. User delays exhausted
6. Service crashes

## Performance Considerations

### Backup Optimization
- Parallel provider execution
- Chunked file uploads
- Incremental backup support
- Compression for small files

### Resource Management
- CPU throttling during business hours
- Bandwidth limiting for OneDrive
- Disk I/O optimization
- Memory-mapped files for large data

## Monitoring and Logging

### Logging Architecture
- Structured JSON logging
- Multiple log levels (ERROR, WARN, INFO, DEBUG)
- Separate logs per component
- Centralized aggregation option

### Metrics Collection
- Backup progress per user
- Error rates by category
- Performance metrics
- OneDrive sync statistics

### Health Checks
- Service heartbeat monitoring
- Agent connectivity checks
- OneDrive API availability
- Disk space monitoring

## Extensibility

### Provider Plugin System
- Interface-based design
- Dynamic provider loading
- Configuration-driven selection
- Version compatibility checks

### Future Enhancements
- Additional backup providers
- Cloud storage alternatives
- Advanced scheduling options
- Machine learning for user behavior