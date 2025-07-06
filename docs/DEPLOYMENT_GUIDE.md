# SCCM Deployment Guide

## Overview

This guide provides step-by-step instructions for deploying the Windows Migration Tool through System Center Configuration Manager (SCCM). The deployment consists of a Windows service, user agents, and associated PowerShell scripts.

## Prerequisites

### Platform Support
**This tool requires Windows x86_64 (64-bit) systems only.** No other platforms, operating systems, or architectures are supported.

### System Requirements
- Windows 10 version 1809+ or Windows 11 (x86_64 only)
- .NET Framework 4.8
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
│   ├── MigrationService.exe.config
│   ├── MigrationAgent.exe
│   ├── MigrationBackup.dll
│   ├── MigrationRestore.exe
│   └── Dependencies/
│       ├── Newtonsoft.Json.dll
│       ├── System.Data.SQLite.dll
│       └── [Other dependencies]
├── Scripts/
│   ├── Install.ps1
│   ├── Uninstall.ps1
│   ├── Detection.ps1
│   └── Repair.ps1
├── Config/
│   ├── ServiceConfig.json
│   └── LogConfig.json
└── Tools/
    └── InstallUtil.exe
```

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
powershell.exe -ExecutionPolicy Bypass -File ".\Scripts\Install.ps1" -LogPath "C:\Windows\Logs\MigrationTool"
```

**Uninstall Program**:
```powershell
powershell.exe -ExecutionPolicy Bypass -File ".\Scripts\Uninstall.ps1" -LogPath "C:\Windows\Logs\MigrationTool"
```

### Step 4: Detection Method

Use the custom detection script:
```powershell
# Detection.ps1
$ServiceName = "MigrationService"
$MinVersion = "1.0.0"

try {
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        Write-Host "Service not found"
        exit 1
    }

    $servicePath = (Get-WmiObject Win32_Service | Where-Object {$_.Name -eq $ServiceName}).PathName
    $serviceExe = $servicePath.Trim('"').Split(' ')[0]
    
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
- .NET Framework 4.8
- Visual C++ Redistributables 2019
- OneDrive for Business (detection script provided)

## Installation Scripts

### Install.ps1
```powershell
[CmdletBinding()]
param(
    [string]$LogPath = "C:\Windows\Logs\MigrationTool"
)

# Start logging
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $LogPath "Install_$timestamp.log"
New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
Start-Transcript -Path $logFile

try {
    Write-Host "Starting Migration Tool installation..."
    
    # Set variables
    $installPath = "C:\Program Files\MigrationTool"
    $serviceName = "MigrationService"
    $serviceDisplayName = "Windows Migration Service"
    $serviceDescription = "Manages user data migration to cloud"
    
    # Create installation directory
    Write-Host "Creating installation directory..."
    New-Item -ItemType Directory -Path $installPath -Force | Out-Null
    
    # Copy files
    Write-Host "Copying files..."
    Copy-Item -Path ".\Binaries\*" -Destination $installPath -Recurse -Force
    Copy-Item -Path ".\Config\*" -Destination "$installPath\Config" -Recurse -Force
    
    # Install service
    Write-Host "Installing Windows service..."
    $servicePath = Join-Path $installPath "MigrationService.exe"
    
    # Use InstallUtil to install the service
    $installUtil = Join-Path $installPath "Tools\InstallUtil.exe"
    & $installUtil /LogFile="$LogPath\ServiceInstall.log" $servicePath
    
    if ($LASTEXITCODE -ne 0) {
        throw "Service installation failed with exit code $LASTEXITCODE"
    }
    
    # Configure service
    Write-Host "Configuring service..."
    $service = Get-Service -Name $serviceName
    Set-Service -Name $serviceName -StartupType Automatic
    Set-Service -Name $serviceName -Description $serviceDescription
    
    # Configure service recovery
    sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000
    
    # Create scheduled task for agent
    Write-Host "Creating user agent scheduled task..."
    $taskName = "MigrationAgent"
    $agentPath = Join-Path $installPath "MigrationAgent.exe"
    
    $action = New-ScheduledTaskAction -Execute $agentPath
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -GroupId "BUILTIN\Users" -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
    
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
    
    # Set permissions
    Write-Host "Setting permissions..."
    $acl = Get-Acl $installPath
    $permission = "BUILTIN\Users","ReadAndExecute","Allow"
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
    $acl.SetAccessRule($accessRule)
    Set-Acl $installPath $acl
    
    # Create registry entries
    Write-Host "Creating registry entries..."
    $regPath = "HKLM:\SOFTWARE\MigrationTool"
    New-Item -Path $regPath -Force | Out-Null
    New-ItemProperty -Path $regPath -Name "Version" -Value "1.0.0" -PropertyType String -Force
    New-ItemProperty -Path $regPath -Name "InstallPath" -Value $installPath -PropertyType String -Force
    New-ItemProperty -Path $regPath -Name "InstallDate" -Value (Get-Date -Format "yyyy-MM-dd") -PropertyType String -Force
    
    # Start service
    Write-Host "Starting service..."
    Start-Service -Name $serviceName
    
    Write-Host "Installation completed successfully!"
    exit 0
}
catch {
    Write-Error "Installation failed: $_"
    exit 1
}
finally {
    Stop-Transcript
}
```

### Uninstall.ps1
```powershell
[CmdletBinding()]
param(
    [string]$LogPath = "C:\Windows\Logs\MigrationTool"
)

# Start logging
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$logFile = Join-Path $LogPath "Uninstall_$timestamp.log"
Start-Transcript -Path $logFile

try {
    Write-Host "Starting Migration Tool uninstallation..."
    
    $installPath = "C:\Program Files\MigrationTool"
    $serviceName = "MigrationService"
    $taskName = "MigrationAgent"
    
    # Stop service
    Write-Host "Stopping service..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    
    # Uninstall service
    Write-Host "Uninstalling service..."
    $servicePath = Join-Path $installPath "MigrationService.exe"
    $installUtil = Join-Path $installPath "Tools\InstallUtil.exe"
    
    if (Test-Path $installUtil) {
        & $installUtil /u /LogFile="$LogPath\ServiceUninstall.log" $servicePath
    }
    
    # Remove scheduled task
    Write-Host "Removing scheduled task..."
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    
    # Remove files
    Write-Host "Removing files..."
    Remove-Item -Path $installPath -Recurse -Force -ErrorAction SilentlyContinue
    
    # Remove registry entries
    Write-Host "Removing registry entries..."
    Remove-Item -Path "HKLM:\SOFTWARE\MigrationTool" -Recurse -Force -ErrorAction SilentlyContinue
    
    Write-Host "Uninstallation completed successfully!"
    exit 0
}
catch {
    Write-Error "Uninstallation failed: $_"
    exit 1
}
finally {
    Stop-Transcript
}
```

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

- Installation logs: `C:\Windows\Logs\MigrationTool\`
- Service logs: `C:\ProgramData\MigrationTool\Logs\`
- Agent logs: `%APPDATA%\MigrationTool\Logs\`
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