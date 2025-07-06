#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the Migration Service on the local computer.

.DESCRIPTION
    This script installs the Migration Service, configures it for automatic startup,
    and sets up recovery options. The service will run under the LOCAL SYSTEM account.

.PARAMETER ServicePath
    Path to the MigrationService.exe file. If not specified, uses the script directory.

.PARAMETER StartService
    If specified, starts the service immediately after installation.

.EXAMPLE
    .\Install-Service.ps1
    Installs the service from the current directory.

.EXAMPLE
    .\Install-Service.ps1 -ServicePath "C:\Program Files\MigrationTool\MigrationService.exe" -StartService
    Installs the service from the specified path and starts it.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [string]$ServicePath,
    
    [Parameter()]
    [switch]$StartService
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'MigrationService'
$ServiceDisplayName = 'Windows Migration Service'
$ServiceDescription = 'Manages user data migration to OneDrive before system reset for Autopilot enrollment'

function Write-ColoredOutput {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        
        [Parameter()]
        [ConsoleColor]$ForegroundColor = 'White'
    )
    
    Write-Host $Message -ForegroundColor $ForegroundColor
}

try {
    Write-ColoredOutput "Migration Service Installer" -ForegroundColor Cyan
    Write-ColoredOutput "=========================" -ForegroundColor Cyan
    Write-Host ""
    
    # Determine service executable path
    if ([string]::IsNullOrEmpty($ServicePath)) {
        $ServicePath = Join-Path $PSScriptRoot "..\MigrationService.exe"
        if (-not (Test-Path $ServicePath)) {
            # Try the publish output directory
            $ServicePath = Join-Path $PSScriptRoot "..\bin\Release\net8.0-windows\win-x64\publish\MigrationService.exe"
        }
    }
    
    # Verify the executable exists
    if (-not (Test-Path $ServicePath)) {
        throw "Service executable not found at: $ServicePath"
    }
    
    $ServicePath = Resolve-Path $ServicePath
    Write-ColoredOutput "Service executable: $ServicePath" -ForegroundColor Gray
    
    # Check if service already exists
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingService) {
        Write-ColoredOutput "Service already exists. Stopping and removing..." -ForegroundColor Yellow
        
        # Stop the service if running
        if ($existingService.Status -ne 'Stopped') {
            Stop-Service -Name $ServiceName -Force
            Write-ColoredOutput "Service stopped" -ForegroundColor Gray
        }
        
        # Remove the service
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }
    
    # Create the service
    Write-ColoredOutput "Creating service..." -ForegroundColor Green
    
    $arguments = @(
        'create'
        $ServiceName
        "binPath= `"$ServicePath`""
        "DisplayName= `"$ServiceDisplayName`""
        'start= auto'
        'obj= LocalSystem'
    )
    
    $result = & sc.exe $arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create service: $result"
    }
    
    # Set service description
    Write-ColoredOutput "Setting service description..." -ForegroundColor Green
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" `
                     -Name "Description" `
                     -Value $ServiceDescription
    
    # Configure recovery options
    Write-ColoredOutput "Configuring recovery options..." -ForegroundColor Green
    
    # Set failure actions: restart after 1 minute for first 3 failures
    & sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
    
    # Enable failure actions
    & sc.exe failureflag $ServiceName 1 | Out-Null
    
    # Set delayed auto-start for better boot performance
    Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" `
                     -Name "DelayedAutostart" `
                     -Value 1 `
                     -Type DWord
    
    # Create necessary directories
    Write-ColoredOutput "Creating directories..." -ForegroundColor Green
    
    $directories = @(
        'C:\ProgramData\MigrationTool'
        'C:\ProgramData\MigrationTool\Data'
        'C:\ProgramData\MigrationTool\Logs'
        'C:\ProgramData\MigrationTool\Backups'
    )
    
    foreach ($dir in $directories) {
        if (-not (Test-Path $dir)) {
            New-Item -Path $dir -ItemType Directory -Force | Out-Null
            Write-ColoredOutput "  Created: $dir" -ForegroundColor Gray
        }
    }
    
    # Set directory permissions
    Write-ColoredOutput "Setting directory permissions..." -ForegroundColor Green
    
    $acl = Get-Acl 'C:\ProgramData\MigrationTool'
    
    # Grant SYSTEM full control
    $systemRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "NT AUTHORITY\SYSTEM",
        "FullControl",
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.SetAccessRule($systemRule)
    
    # Grant Administrators full control
    $adminRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "BUILTIN\Administrators",
        "FullControl",
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.SetAccessRule($adminRule)
    
    # Grant Users read access (for agent communication)
    $usersRule = New-Object System.Security.AccessControl.FileSystemAccessRule(
        "BUILTIN\Users",
        "ReadAndExecute",
        "ContainerInherit,ObjectInherit",
        "None",
        "Allow"
    )
    $acl.SetAccessRule($usersRule)
    
    Set-Acl 'C:\ProgramData\MigrationTool' $acl
    
    # Start the service if requested
    if ($StartService) {
        Write-ColoredOutput "Starting service..." -ForegroundColor Green
        Start-Service -Name $ServiceName
        
        # Wait for service to start
        $timeout = 30
        $timer = [Diagnostics.Stopwatch]::StartNew()
        
        while ((Get-Service -Name $ServiceName).Status -ne 'Running') {
            if ($timer.Elapsed.TotalSeconds -gt $timeout) {
                throw "Service failed to start within $timeout seconds"
            }
            Start-Sleep -Milliseconds 500
        }
        
        Write-ColoredOutput "Service started successfully" -ForegroundColor Green
    }
    
    # Display final status
    Write-Host ""
    Write-ColoredOutput "Installation completed successfully!" -ForegroundColor Green
    Write-Host ""
    
    $service = Get-Service -Name $ServiceName
    Write-ColoredOutput "Service Information:" -ForegroundColor Cyan
    Write-Host "  Name:         $($service.Name)"
    Write-Host "  Display Name: $($service.DisplayName)"
    Write-Host "  Status:       $($service.Status)"
    Write-Host "  Start Type:   $($service.StartType)"
    Write-Host ""
    
    if (-not $StartService) {
        Write-ColoredOutput "To start the service, run:" -ForegroundColor Yellow
        Write-Host "  Start-Service -Name $ServiceName"
        Write-Host ""
    }
}
catch {
    Write-ColoredOutput "ERROR: $_" -ForegroundColor Red
    
    # Attempt cleanup on failure
    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-ColoredOutput "Attempting to remove partially installed service..." -ForegroundColor Yellow
        & sc.exe delete $ServiceName 2>$null | Out-Null
    }
    
    exit 1
}