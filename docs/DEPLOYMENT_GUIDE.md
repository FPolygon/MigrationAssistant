# SCCM Deployment Guide

## Overview

This guide provides step-by-step instructions for deploying the Windows Migration Tool through System Center Configuration Manager (SCCM). The deployment consists of a Windows service, user agents, and associated PowerShell scripts.

## Prerequisites

### Platform Support
**This tool requires Windows x86_64 (64-bit) systems only.** No other platforms, operating systems, or architectures are supported.

### System Requirements
- Windows 10 version 1809+ or Windows 11 (x86_64 only)
- .NET 8.0 Runtime (included in deployment package)
- PowerShell 5.1 or higher
- OneDrive for Business client installed
- SCCM client installed and functioning

### Required Permissions
- SCCM application deployment rights
- Local administrator rights on target systems
- Access to create SCCM packages and programs

## Package Structure

```
MigrationTool_Package/
├── Binaries/
│   ├── MigrationService.exe
│   ├── MigrationService.dll
│   ├── MigrationService.deps.json
│   ├── MigrationService.runtimeconfig.json
│   ├── appsettings.json
│   └── Dependencies/
│       ├── Newtonsoft.Json.dll
│       ├── System.Data.SQLite.dll
│       ├── Microsoft.Extensions.*.dll
│       └── [Other .NET dependencies]
├── PowerShell/
│   └── Deploy-Migration.ps1
├── Scripts/
│   ├── Manage-Service.ps1
│   ├── Install-Service.ps1
│   └── Uninstall-Service.ps1
└── Config/
    └── appsettings.json
```

**Note**: MigrationAgent.exe, MigrationBackup.dll, and MigrationRestore.exe will be added in Phase 2 and later phases.

## SCCM Application Creation

### Step 1: Create Application

1. In SCCM Console, navigate to **Software Library > Application Management > Applications**
2. Right-click and select **Create Application**
3. Choose **Manually specify the application information**
4. Enter application details:
   - Name: `Windows Migration Tool`
   - Publisher: `IT Department`
   - Version: `1.0.0`

### Step 2: Create Deployment Type

1. Add a deployment type
2. Select **Script Installer**
3. Configure settings:
   - Name: `Migration Tool - Script Install`
   - Content location: `\\SCCMServer\Sources\Apps\MigrationTool`

### Step 3: Configure Installation Program

**Installation Program**:
```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\PowerShell\Deploy-Migration.ps1" -Action Install -LogPath "C:\Windows\Logs\MigrationTool"
```

**Uninstall Program**:
```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\PowerShell\Deploy-Migration.ps1" -Action Uninstall -LogPath "C:\Windows\Logs\MigrationTool"
```

### Step 4: Detection Method

Use the custom detection script:
```powershell
# Detection.ps1
$ServiceName = "MigrationService"
$MinVersion = "1.0.0"
$InstallPath = "C:\Program Files\MigrationAssistant"

try {
    # Check if service exists
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Write-Host "Service not found"
        exit 1
    }

    # Check installation directory
    if (!(Test-Path $InstallPath)) {
        Write-Host "Installation directory not found"
        exit 1
    }

    # Check service executable
    $serviceExe = Join-Path $InstallPath "MigrationService.exe"
    if (Test-Path $serviceExe) {
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($serviceExe).FileVersion
        if ([Version]$version -ge [Version]$MinVersion) {
            Write-Host "Migration Tool $version is installed"
            exit 0
        }
    }
}
catch {
    Write-Host "Detection failed: $_"
}
exit 1
```

### Step 5: User Experience Settings

- Installation behavior: **Install for system**
- Logon requirement: **Whether or not a user is logged on**
- Installation program visibility: **Hidden**
- Maximum allowed run time: **120 minutes**
- Estimated installation time: **15 minutes**

### Step 6: Requirements

Add requirements rules:
- OS Version: Windows 10 1809+ or Windows 11 (x86_64 only)
- Architecture: x64 (64-bit) only
- Disk space: Minimum 500 MB free
- RAM: Minimum 4 GB

### Step 7: Dependencies

Add dependencies:
- .NET 8.0 Desktop Runtime (Windows)
- Visual C++ Redistributables 2019
- OneDrive for Business (detection script provided)

## Installation Scripts

The deployment is handled by the master deployment script `Deploy-Migration.ps1` which supports multiple actions:

### Deploy-Migration.ps1 Usage

```powershell
# Install the Migration Tool
.\Deploy-Migration.ps1 -Action Install

# Install with custom log path
.\Deploy-Migration.ps1 -Action Install -LogPath "C:\CustomLogs"

# Install in test mode (doesn't start service)
.\Deploy-Migration.ps1 -Action Install -TestMode

# Uninstall the Migration Tool
.\Deploy-Migration.ps1 -Action Uninstall

# Repair installation
.\Deploy-Migration.ps1 -Action Repair

# Force action without prompts
.\Deploy-Migration.ps1 -Action Uninstall -Force
```

### Key Features of Deploy-Migration.ps1

1. **Prerequisite Checking**:
   - Validates Windows version (10 1809+)
   - Checks .NET 8.0 runtime
   - Verifies PowerShell version
   - Ensures admin privileges

2. **Installation Process**:
   - Creates installation directory at `C:\Program Files\MigrationAssistant`
   - Copies all binaries and dependencies
   - Registers Windows service using service installer
   - Configures service recovery options
   - Creates scheduled task for agent (Phase 2)
   - Sets appropriate permissions
   - Creates registry entries for tracking

3. **Uninstallation Process**:
   - Stops running service
   - Unregisters Windows service
   - Removes scheduled tasks
   - Cleans up files and directories
   - Removes registry entries

4. **Logging**:
   - Comprehensive logging to specified path
   - Timestamped log files
   - Console output with color coding
   - Separate logs for each action

5. **Error Handling**:
   - Transaction-based operations
   - Rollback on failure
   - Detailed error reporting
   - Exit codes for SCCM integration

## Deployment Configuration

### Collections

Create targeted collections:
1. **Pilot Group**: Initial test deployment
2. **Phase 1**: Low-risk users
3. **Phase 2**: Standard users
4. **Phase 3**: High-priority users

### Deployment Settings

1. **Purpose**: Required
2. **Deploy automatically**: According to schedule
3. **Schedule**: 
   - Available time: Immediate
   - Installation deadline: 7 days
4. **User notifications**: Display in Software Center
5. **Restart behavior**: No restart required

### Maintenance Windows

Configure maintenance windows to avoid business hours:
- Weekdays: 6:00 PM - 6:00 AM
- Weekends: All day

## Monitoring and Reporting

### Built-in Reports

Use these SCCM reports:
- Application Deployment Status
- Application Installation Errors
- Computers with Specific Application

### Custom Queries

**Find computers with Migration Tool**:
```sql
SELECT 
    SYS.Name0 AS 'Computer Name',
    APP.DisplayName0 AS 'Application',
    APP.Version0 AS 'Version',
    APP.InstallDate0 AS 'Install Date'
FROM v_R_System SYS
JOIN v_Add_Remove_Programs APP ON SYS.ResourceID = APP.ResourceID
WHERE APP.DisplayName0 LIKE '%Migration Tool%'
ORDER BY SYS.Name0
```

**Check service status**:
```sql
SELECT 
    SYS.Name0 AS 'Computer Name',
    SVC.DisplayName0 AS 'Service Name',
    SVC.State0 AS 'State',
    SVC.StartMode0 AS 'Start Mode'
FROM v_R_System SYS
JOIN v_GS_SERVICE SVC ON SYS.ResourceID = SVC.ResourceID
WHERE SVC.Name0 = 'MigrationService'
ORDER BY SYS.Name0
```

## Troubleshooting

### Common Issues

**Service fails to start**:
- Check Windows Event Log
- Verify .NET Framework 4.8 is installed
- Check service account permissions
- Review installation log

**Agent doesn't appear for users**:
- Verify scheduled task exists
- Check task triggers
- Review agent log files
- Ensure user has logged off/on

**OneDrive detection fails**:
- Verify OneDrive is installed
- Check if user is signed in
- Review OneDrive sync status
- Check registry keys

### Log Locations

- Installation logs: `C:\Windows\Logs\MigrationAssistant\`
- Service logs: `C:\ProgramData\MigrationAssistant\Logs\`
- Agent logs: `%APPDATA%\MigrationAssistant\Logs\` (Phase 2)
- SCCM logs: `C:\Windows\CCM\Logs\`

### Support Process

1. Check deployment status in SCCM
2. Review installation logs
3. Verify service is running
4. Check agent scheduled task
5. Review service and agent logs
6. Contact development team if needed

## Security Considerations

### Service Account

The service runs as LOCAL SYSTEM for:
- Access to all user profiles
- Registry modification rights
- File system access

### Data Protection

- Backup data encrypted in transit
- Credentials protected with DPAPI
- Logs sanitized of sensitive data
- Named pipes secured with ACLs

### Audit Requirements

- All operations logged
- User consent tracked
- IT escalations recorded
- Compliance reporting available