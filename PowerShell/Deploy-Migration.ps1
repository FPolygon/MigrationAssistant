#Requires -Version 5.1
#Requires -RunAsAdministrator

<#
.SYNOPSIS
    Master deployment script for Windows Migration Tool

.DESCRIPTION
    This script handles installation, uninstallation, and repair of the Windows Migration Tool.
    It must be run with administrative privileges.

.PARAMETER Action
    The deployment action to perform (Install, Uninstall, Repair)

.PARAMETER ConfigPath
    Path to the deployment configuration file

.PARAMETER LogPath
    Directory path for log files

.PARAMETER Force
    Force the action without prompts

.PARAMETER TestMode
    Install without starting the service (for testing)

.EXAMPLE
    .\Deploy-Migration.ps1 -Action Install
    Installs the Migration Tool with default settings

.EXAMPLE
    .\Deploy-Migration.ps1 -Action Uninstall -Force
    Uninstalls the Migration Tool without prompts
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory=$false)]
    [ValidateSet('Install', 'Uninstall', 'Repair')]
    [string]$Action = "Install",
    
    [Parameter(Mandatory=$false)]
    [string]$ConfigPath = ".\Config\DeployConfig.json",
    
    [Parameter(Mandatory=$false)]
    [string]$LogPath = "C:\Windows\Logs\MigrationTool",
    
    [Parameter(Mandatory=$false)]
    [switch]$Force,
    
    [Parameter(Mandatory=$false)]
    [switch]$TestMode
)

#region Functions

function Write-Log {
    param(
        [string]$Message,
        [string]$Level = "INFO"
    )
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    
    # Console output
    switch ($Level) {
        "ERROR" { Write-Host $logMessage -ForegroundColor Red }
        "WARN"  { Write-Host $logMessage -ForegroundColor Yellow }
        "INFO"  { Write-Host $logMessage -ForegroundColor Green }
        default { Write-Host $logMessage }
    }
    
    # File output
    if ($script:LogFile) {
        Add-Content -Path $script:LogFile -Value $logMessage
    }
}

function Test-Prerequisite {
    Write-Log -Message "Checking prerequisites..."
    
    $errors = @()
    
    # Check OS version
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $osVersion = [version]$os.Version
    if ($osVersion -lt [version]"10.0.17763") {
        $errors += "Windows 10 version 1809 or higher is required"
    }
    
    # Check .NET Framework
    $dotNetVersion = Get-ItemProperty "HKLM:SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\" -Name Release -ErrorAction SilentlyContinue
    if (!$dotNetVersion -or $dotNetVersion.Release -lt 528040) {
        $errors += ".NET Framework 4.8 or higher is required"
    }
    
    # Check PowerShell version
    if ($PSVersionTable.PSVersion.Major -lt 5) {
        $errors += "PowerShell 5.0 or higher is required"
    }
    
    # Check if running as admin (redundant with #Requires but kept for explicit error message)
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $errors += "This script must be run as Administrator"
    }
    
    # Check OneDrive
    $oneDrive = Get-Process -Name OneDrive -ErrorAction SilentlyContinue
    if (-not $oneDrive) {
        Write-Log -Message "OneDrive is not running - will check during user backup" -Level "WARN"
    }
    
    if ($errors.Count -gt 0) {
        foreach ($error in $errors) {
            Write-Log -Message $error -Level "ERROR"
        }
        return $false
    }
    
    Write-Log -Message "All prerequisites met"
    return $true
}

function Install-MigrationTool {
    Write-Log -Message "Installing Migration Tool..."
    
    try {
        # Create installation directory
        $installDir = "C:\Program Files\MigrationTool"
        if (-not (Test-Path -Path $installDir)) {
            New-Item -ItemType Directory -Path $installDir -Force | Out-Null
            Write-Log -Message "Created installation directory: $installDir"
        }
        
        # Copy binaries
        Write-Log -Message "Copying binaries..."
        Copy-Item -Path ".\Binaries\*" -Destination $installDir -Recurse -Force
        
        # Copy configuration
        $configDir = "$installDir\Config"
        New-Item -ItemType Directory -Path $configDir -Force | Out-Null
        Copy-Item -Path ".\Config\*" -Destination $configDir -Recurse -Force
        
        # Install Windows Service
        Write-Log -Message "Installing Windows Service..."
        $servicePath = "$installDir\MigrationService.exe"
        $serviceName = "MigrationService"
        
        # Check if service already exists
        $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($existingService) {
            Write-Log -Message "Service already exists, stopping and removing..."
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            $null = sc.exe delete $serviceName
            Start-Sleep -Seconds 2
        }
        
        # Create service
        New-Service -Name $serviceName `
                   -BinaryPathName $servicePath `
                   -DisplayName "Windows Migration Service" `
                   -Description "Manages user data migration to cloud" `
                   -StartupType Automatic `
                   -ErrorAction Stop
        
        Write-Log -Message "Service created successfully"
        
        # Configure service recovery
        $null = sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000
        
        # Create scheduled task for agent
        Install-AgentTask
        
        # Set permissions
        Set-InstallationPermission -Path $installDir
        
        # Create registry entries
        New-RegistryEntry
        
        # Initialize database
        Initialize-Database -Path "$installDir\Data"
        
        # Start service
        if (-not $TestMode) {
            Write-Log -Message "Starting service..."
            Start-Service -Name $serviceName
            Write-Log -Message "Service started successfully"
        }
        
        Write-Log -Message "Migration Tool installed successfully"
        return $true
    }
    catch {
        Write-Log -Message "Installation failed: $_" -Level "ERROR"
        return $false
    }
}

function Install-AgentTask {
    Write-Log -Message "Creating agent scheduled task..."
    
    $taskName = "MigrationAgent"
    $agentPath = "C:\Program Files\MigrationTool\MigrationAgent.exe"
    
    # Remove existing task if present
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    
    # Create task XML
    $taskXml = @'
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Migration Tool User Agent - Displays migration notifications and manages user backup</Description>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <GroupId>S-1-5-32-545</GroupId>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>{0}</Command>
      <Arguments>--startup</Arguments>
    </Exec>
  </Actions>
</Task>
'@ -f $agentPath
    
    # Register task
    $null = Register-ScheduledTask -Xml $taskXml -TaskName $taskName -Force
    Write-Log -Message "Agent scheduled task created"
}

function Set-InstallationPermission {
    [CmdletBinding(SupportsShouldProcess)]
    param([string]$Path)
    
    Write-Log -Message "Setting permissions on $Path..."
    
    if ($PSCmdlet.ShouldProcess($Path, "Set file system permissions")) {
        $acl = Get-Acl $Path
        
        # Add read/execute for Users
        $permission = "BUILTIN\Users","ReadAndExecute","ContainerInherit,ObjectInherit","None","Allow"
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
        $acl.SetAccessRule($accessRule)
        
        # Add full control for SYSTEM
        $permission = "NT AUTHORITY\SYSTEM","FullControl","ContainerInherit,ObjectInherit","None","Allow"
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
        $acl.SetAccessRule($accessRule)
        
        # Add full control for Administrators
        $permission = "BUILTIN\Administrators","FullControl","ContainerInherit,ObjectInherit","None","Allow"
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
        $acl.SetAccessRule($accessRule)
        
        Set-Acl $Path $acl
        Write-Log -Message "Permissions set successfully"
    }
}

function New-RegistryEntry {
    [CmdletBinding(SupportsShouldProcess)]
    param()
    
    Write-Log -Message "Creating registry entries..."
    
    $regPath = "HKLM:\SOFTWARE\MigrationTool"
    
    if ($PSCmdlet.ShouldProcess($regPath, "Create registry key")) {
        if (-not (Test-Path -Path $regPath)) {
            $null = New-Item -Path $regPath -Force
        }
    }
    
    # Set registry values
    if ($PSCmdlet.ShouldProcess($regPath, "Set registry values")) {
        Set-ItemProperty -Path $regPath -Name "Version" -Value "1.0.0"
        Set-ItemProperty -Path $regPath -Name "InstallPath" -Value "C:\Program Files\MigrationTool"
        Set-ItemProperty -Path $regPath -Name "InstallDate" -Value (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
        Set-ItemProperty -Path $regPath -Name "ConfigPath" -Value "C:\Program Files\MigrationTool\Config"
        Set-ItemProperty -Path $regPath -Name "LogPath" -Value "C:\ProgramData\MigrationTool\Logs"
        
        Write-Log -Message "Registry entries created"
    }
}

function Initialize-Database {
    param([string]$Path)
    
    Write-Log -Message "Initializing database..."
    
    if (-not (Test-Path -Path $Path)) {
        $null = New-Item -ItemType Directory -Path $Path -Force
    }
    
    # Database will be created by service on first run
    # Just ensure directory exists and has correct permissions
    Set-InstallationPermission -Path $Path
    
    Write-Log -Message "Database directory prepared"
}

function Uninstall-MigrationTool {
    Write-Log -Message "Uninstalling Migration Tool..."
    
    try {
        $serviceName = "MigrationService"
        $taskName = "MigrationAgent"
        $installDir = "C:\Program Files\MigrationTool"
        
        # Stop and remove service
        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($service) {
            Write-Log -Message "Stopping service..."
            Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            
            Write-Log -Message "Removing service..."
            $null = sc.exe delete $serviceName
            Start-Sleep -Seconds 2
        }
        
        # Remove scheduled task
        Write-Log -Message "Removing scheduled task..."
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        
        # Remove files
        if (Test-Path $installDir) {
            Write-Log -Message "Removing installation files..."
            Remove-Item -Path $installDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        
        # Remove data directory
        $dataDir = "C:\ProgramData\MigrationTool"
        if (Test-Path $dataDir) {
            Write-Log -Message "Removing data files..."
            Remove-Item -Path $dataDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        
        # Remove registry entries
        Write-Log -Message "Removing registry entries..."
        Remove-Item -Path "HKLM:\SOFTWARE\MigrationTool" -Recurse -Force -ErrorAction SilentlyContinue
        
        Write-Log -Message "Migration Tool uninstalled successfully"
        return $true
    }
    catch {
        Write-Log -Message "Uninstallation failed: $_" -Level "ERROR"
        return $false
    }
}

#endregion

#region Main Script

# Initialize logging
$script:LogFile = Join-Path $LogPath "Deploy-Migration_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"
$null = New-Item -ItemType Directory -Path $LogPath -Force -ErrorAction SilentlyContinue

Write-Log -Message "========================================="
Write-Log -Message "Migration Tool Deployment Script"
Write-Log -Message "Action: $Action"
Write-Log -Message "========================================="

# Check prerequisites
if (-not (Test-Prerequisite)) {
    Write-Log -Message "Prerequisites check failed" -Level "ERROR"
    exit 1
}

# Perform action
$result = $false
switch ($Action.ToLower()) {
    "install" {
        $result = Install-MigrationTool
    }
    "uninstall" {
        $result = Uninstall-MigrationTool
    }
    "repair" {
        Write-Log -Message "Performing repair installation..."
        $null = Uninstall-MigrationTool
        $result = Install-MigrationTool
    }
    default {
        Write-Log -Message "Invalid action: $Action" -Level "ERROR"
        Write-Log -Message "Valid actions: Install, Uninstall, Repair"
        exit 1
    }
}

# Exit with appropriate code
if ($result) {
    Write-Log -Message "Deployment completed successfully"
    exit 0
} else {
    Write-Log -Message "Deployment failed" -Level "ERROR"
    exit 1
}

#endregion