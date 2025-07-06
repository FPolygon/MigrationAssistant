# API Design

## Overview

This document defines the interfaces and contracts between the various components of the Migration Tool. All APIs use JSON for data exchange and follow RESTful principles where applicable.

## Inter-Process Communication (IPC)

### Named Pipe Protocol

**Pipe Name**: `\\.\pipe\MigrationService_{ComputerName}`

**Message Format**:
```json
{
  "id": "unique-message-id",
  "type": "MESSAGE_TYPE",
  "timestamp": "2024-01-10T10:00:00Z",
  "payload": { }
}
```

### Message Types

#### Service → Agent Messages

**BACKUP_REQUEST**
```json
{
  "type": "BACKUP_REQUEST",
  "payload": {
    "userId": "user-sid",
    "priority": "normal|high|urgent",
    "deadline": "2024-01-15T10:00:00Z",
    "categories": ["files", "browsers", "email", "system"]
  }
}
```

**STATUS_UPDATE**
```json
{
  "type": "STATUS_UPDATE",
  "payload": {
    "overallStatus": "waiting|in_progress|blocked|ready",
    "blockingUsers": ["user1", "user2"],
    "readyUsers": ["user3"],
    "totalUsers": 4
  }
}
```

**ESCALATION_NOTICE**
```json
{
  "type": "ESCALATION_NOTICE",
  "payload": {
    "reason": "quota_exceeded|sync_error|large_pst|timeout",
    "details": "Detailed error message",
    "ticketNumber": "INC0012345"
  }
}
```

#### Agent → Service Messages

**BACKUP_STARTED**
```json
{
  "type": "BACKUP_STARTED",
  "payload": {
    "userId": "user-sid",
    "categories": ["files", "browsers"],
    "estimatedSizeMB": 2048
  }
}
```

**BACKUP_PROGRESS**
```json
{
  "type": "BACKUP_PROGRESS",
  "payload": {
    "userId": "user-sid",
    "category": "files",
    "progress": 45.5,
    "currentFile": "Documents\\Report.docx",
    "bytesTransferred": 1073741824,
    "bytesTotal": 2147483648
  }
}
```

**BACKUP_COMPLETED**
```json
{
  "type": "BACKUP_COMPLETED",
  "payload": {
    "userId": "user-sid",
    "success": true,
    "manifestPath": "OneDrive\\MigrationBackup\\manifest.json",
    "categories": {
      "files": { "success": true, "itemCount": 1523 },
      "browsers": { "success": true, "itemCount": 3 },
      "email": { "success": false, "error": "PST file too large" }
    }
  }
}
```

**DELAY_REQUEST**
```json
{
  "type": "DELAY_REQUEST",
  "payload": {
    "userId": "user-sid",
    "reason": "user_busy|need_time|other",
    "requestedDelay": 86400,
    "delaysUsed": 2
  }
}
```

## Service Interfaces

### IUserProfileManager

```csharp
public interface IUserProfileManager
{
    Task<List<UserProfile>> GetAllProfilesAsync();
    Task<UserProfile> GetProfileAsync(string userSid);
    Task<bool> IsActiveUserAsync(UserProfile profile);
    Task<ProfileMetrics> CalculateMetricsAsync(UserProfile profile);
    Task UpdateProfileStatusAsync(string userSid, ProfileStatus status);
}

public class UserProfile
{
    public string Sid { get; set; }
    public string Username { get; set; }
    public string ProfilePath { get; set; }
    public DateTime LastLogin { get; set; }
    public ProfileType Type { get; set; }
    public ProfileStatus Status { get; set; }
}

public class ProfileMetrics
{
    public long ProfileSizeMB { get; set; }
    public DateTime LastActivity { get; set; }
    public int ActiveProcessCount { get; set; }
    public bool HasRecentActivity { get; set; }
}

public enum ProfileStatus
{
    Unknown,
    Active,
    Inactive,
    BackupPending,
    BackupInProgress,
    BackupComplete,
    BackupFailed
}
```

### IMigrationOrchestrator

```csharp
public interface IMigrationOrchestrator
{
    Task<MigrationStatus> GetStatusAsync();
    Task<bool> CanResetAsync();
    Task InitiateMigrationAsync();
    Task RequestUserBackupAsync(string userSid);
    Task<BackupResult> GetUserBackupStatusAsync(string userSid);
    Task HandleDelayRequestAsync(string userSid, TimeSpan delay);
    Task TriggerEscalationAsync(EscalationReason reason, string details);
}

public class MigrationStatus
{
    public string MigrationId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime Deadline { get; set; }
    public MigrationState State { get; set; }
    public List<UserBackupStatus> UserStatuses { get; set; }
    public bool CanReset { get; set; }
    public List<string> BlockingUsers { get; set; }
}

public enum MigrationState
{
    NotStarted,
    InProgress,
    WaitingForUsers,
    ReadyForReset,
    Escalated,
    Completed
}
```

### IBackupProvider

```csharp
public interface IBackupProvider
{
    string Category { get; }
    Task<bool> CanBackupAsync(BackupContext context);
    Task<BackupEstimate> EstimateAsync(BackupContext context);
    Task<BackupResult> BackupAsync(BackupContext context, IProgress<BackupProgress> progress);
    Task<RestoreResult> RestoreAsync(RestoreContext context, IProgress<RestoreProgress> progress);
}

public class BackupContext
{
    public UserProfile User { get; set; }
    public string BackupPath { get; set; }
    public BackupOptions Options { get; set; }
}

public class BackupEstimate
{
    public long EstimatedSizeMB { get; set; }
    public int EstimatedItems { get; set; }
    public TimeSpan EstimatedDuration { get; set; }
    public List<string> Warnings { get; set; }
}

public class BackupResult
{
    public bool Success { get; set; }
    public string ManifestPath { get; set; }
    public long ActualSizeMB { get; set; }
    public int ItemsBackedUp { get; set; }
    public List<BackupError> Errors { get; set; }
}

public class BackupProgress
{
    public double PercentComplete { get; set; }
    public string CurrentItem { get; set; }
    public long BytesTransferred { get; set; }
    public long BytesTotal { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan EstimatedTimeRemaining { get; set; }
}
```

### IOneDriveManager

```csharp
public interface IOneDriveManager
{
    Task<OneDriveStatus> GetStatusAsync(string userSid);
    Task<long> GetAvailableSpaceMBAsync(string userSid);
    Task<bool> EnsureFolderSyncedAsync(string folderPath);
    Task<SyncProgress> GetSyncProgressAsync(string folderPath);
    Task ForceSyncAsync(string folderPath);
    Task<bool> WaitForSyncAsync(string folderPath, TimeSpan timeout);
}

public class OneDriveStatus
{
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public bool IsSignedIn { get; set; }
    public string AccountEmail { get; set; }
    public string SyncFolder { get; set; }
    public OneDriveSyncStatus SyncStatus { get; set; }
}

public enum OneDriveSyncStatus
{
    Unknown,
    UpToDate,
    Syncing,
    Paused,
    Error
}
```

### INotificationManager

```csharp
public interface INotificationManager
{
    Task ShowNotificationAsync(NotificationRequest request);
    Task<bool> ShouldShowNotificationAsync();
    Task<DelayResponse> RequestDelayAsync(DelayRequest request);
    Task UpdateProgressAsync(BackupProgress progress);
    Task HideNotificationAsync();
}

public class NotificationRequest
{
    public NotificationType Type { get; set; }
    public string Title { get; set; }
    public string Message { get; set; }
    public NotificationPriority Priority { get; set; }
    public List<NotificationAction> Actions { get; set; }
}

public class SmartDetectionResult
{
    public bool InMeeting { get; set; }
    public bool CameraActive { get; set; }
    public bool MicrophoneActive { get; set; }
    public bool FullscreenApp { get; set; }
    public bool CalendarBlocked { get; set; }
    public string BlockingReason { get; set; }
}
```

## Backup Manifest Schema

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["version", "metadata", "categories"],
  "properties": {
    "version": {
      "type": "string",
      "pattern": "^\\d+\\.\\d+$"
    },
    "metadata": {
      "type": "object",
      "required": ["migrationId", "userId", "computerName", "backupDate"],
      "properties": {
        "migrationId": { "type": "string" },
        "userId": { "type": "string" },
        "computerName": { "type": "string" },
        "backupDate": { "type": "string", "format": "date-time" },
        "toolVersion": { "type": "string" }
      }
    },
    "categories": {
      "type": "object",
      "properties": {
        "files": {
          "type": "object",
          "properties": {
            "items": { "type": "integer" },
            "sizeMB": { "type": "integer" },
            "checksum": { "type": "string" },
            "folders": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "path": { "type": "string" },
                  "itemCount": { "type": "integer" },
                  "sizeMB": { "type": "integer" }
                }
              }
            }
          }
        },
        "browsers": {
          "type": "object",
          "additionalProperties": {
            "type": "object",
            "properties": {
              "profiles": { "type": "integer" },
              "bookmarks": { "type": "integer" },
              "passwords": { "type": "integer" },
              "extensions": { "type": "array", "items": { "type": "string" } },
              "syncAccount": { "type": "string" }
            }
          }
        },
        "email": {
          "type": "object",
          "properties": {
            "outlookVersion": { "type": "string" },
            "profiles": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "emailAddress": { "type": "string" },
                  "accountType": { "type": "string" },
                  "pstFiles": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "filename": { "type": "string" },
                        "sizeMB": { "type": "integer" },
                        "backed_up": { "type": "boolean" }
                      }
                    }
                  }
                }
              }
            }
          }
        },
        "system": {
          "type": "object",
          "properties": {
            "wifiProfiles": { "type": "integer" },
            "credentials": { "type": "integer" },
            "networkDrives": { "type": "integer" },
            "printers": { "type": "integer" }
          }
        }
      }
    },
    "errors": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "category": { "type": "string" },
          "error": { "type": "string" },
          "details": { "type": "string" },
          "timestamp": { "type": "string", "format": "date-time" }
        }
      }
    }
  }
}
```

## REST API (Future Enhancement)

### Endpoints

**GET /api/migration/status**
```json
Response:
{
  "migrationId": "550e8400-e29b-41d4-a716-446655440000",
  "state": "in_progress",
  "startDate": "2024-01-10T08:00:00Z",
  "deadline": "2024-01-17T17:00:00Z",
  "canReset": false,
  "totalUsers": 5,
  "completedUsers": 3,
  "blockingUsers": ["user1", "user2"]
}
```

**GET /api/migration/users**
```json
Response:
{
  "users": [
    {
      "sid": "S-1-5-21-...",
      "username": "john.doe",
      "status": "backup_complete",
      "lastActivity": "2024-01-10T14:30:00Z",
      "backupSizeMB": 2048
    }
  ]
}
```

**POST /api/migration/escalate**
```json
Request:
{
  "reason": "manual",
  "details": "User requesting IT assistance",
  "userId": "S-1-5-21-..."
}

Response:
{
  "ticketNumber": "INC0012345",
  "message": "IT has been notified"
}
```

## Error Codes

| Code | Name | Description |
|------|------|-------------|
| 1001 | ONEDRIVE_NOT_FOUND | OneDrive installation not detected |
| 1002 | ONEDRIVE_NOT_SIGNED_IN | User not signed into OneDrive |
| 1003 | ONEDRIVE_QUOTA_EXCEEDED | Insufficient OneDrive space |
| 1004 | ONEDRIVE_SYNC_ERROR | OneDrive sync failure |
| 2001 | BACKUP_PROVIDER_ERROR | Provider-specific backup error |
| 2002 | BACKUP_TIMEOUT | Backup operation timed out |
| 2003 | BACKUP_CANCELLED | User cancelled backup |
| 3001 | IPC_CONNECTION_FAILED | Cannot connect to service |
| 3002 | IPC_TIMEOUT | IPC operation timed out |
| 4001 | ESCALATION_FAILED | Cannot contact IT system |
| 5001 | RESTORE_MANIFEST_INVALID | Corrupt or missing manifest |
| 5002 | RESTORE_CHECKSUM_MISMATCH | Data integrity check failed |