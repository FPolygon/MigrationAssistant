#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Uninstalls the Migration Service from the local computer.

.DESCRIPTION
    This script stops and removes the Migration Service. Optionally, it can also
    remove the service data and logs.

.PARAMETER RemoveData
    If specified, removes all service data including logs and database files.
    Use with caution as this cannot be undone.

.PARAMETER Force
    If specified, forces the uninstallation even if the service is in use.

.EXAMPLE
    .\Uninstall-Service.ps1
    Uninstalls the service but preserves data files.

.EXAMPLE
    .\Uninstall-Service.ps1 -RemoveData -Force
    Forcefully uninstalls the service and removes all data.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter()]
    [switch]$RemoveData,
    
    [Parameter()]
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ServiceName = 'MigrationService'

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
    Write-ColoredOutput "Migration Service Uninstaller" -ForegroundColor Cyan
    Write-ColoredOutput "============================" -ForegroundColor Cyan
    Write-Host ""
    
    # Check if service exists
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if (-not $service) {
        Write-ColoredOutput "Service '$ServiceName' is not installed" -ForegroundColor Yellow
        exit 0
    }
    
    # Stop dependent services first
    $dependentServices = Get-Service -Name $ServiceName -DependentServices -ErrorAction SilentlyContinue
    if ($dependentServices) {
        Write-ColoredOutput "Stopping dependent services..." -ForegroundColor Yellow
        foreach ($depService in $dependentServices) {
            if ($depService.Status -ne 'Stopped') {
                Stop-Service -Name $depService.Name -Force:$Force
                Write-ColoredOutput "  Stopped: $($depService.DisplayName)" -ForegroundColor Gray
            }
        }
    }
    
    # Stop the service
    if ($service.Status -ne 'Stopped') {
        Write-ColoredOutput "Stopping service..." -ForegroundColor Yellow
        
        if ($Force) {
            Stop-Service -Name $ServiceName -Force
        } else {
            Stop-Service -Name $ServiceName -PassThru | Out-Null
        }
        
        # Wait for service to stop
        $timeout = 30
        $timer = [Diagnostics.Stopwatch]::StartNew()
        
        while ((Get-Service -Name $ServiceName).Status -ne 'Stopped') {
            if ($timer.Elapsed.TotalSeconds -gt $timeout) {
                if ($Force) {
                    # Force kill the process
                    $process = Get-Process -Name "MigrationService" -ErrorAction SilentlyContinue
                    if ($process) {
                        $process | Stop-Process -Force
                        Write-ColoredOutput "Force killed service process" -ForegroundColor Red
                    }
                } else {
                    throw "Service failed to stop within $timeout seconds. Use -Force to override."
                }
            }
            Start-Sleep -Milliseconds 500
        }
        
        Write-ColoredOutput "Service stopped" -ForegroundColor Green
    }
    
    # Remove the service
    Write-ColoredOutput "Removing service..." -ForegroundColor Yellow
    
    $result = & sc.exe delete $ServiceName
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to delete service: $result"
    }
    
    # Wait for service to be removed from registry
    Start-Sleep -Seconds 2
    
    Write-ColoredOutput "Service removed" -ForegroundColor Green
    
    # Remove data if requested
    if ($RemoveData) {
        if ($PSCmdlet.ShouldProcess("Service data and logs", "Remove")) {
            Write-ColoredOutput "Removing service data..." -ForegroundColor Yellow
            
            $dataPath = 'C:\ProgramData\MigrationTool'
            
            if (Test-Path $dataPath) {
                # First, try to remove normally
                try {
                    Remove-Item -Path $dataPath -Recurse -Force
                    Write-ColoredOutput "Data removed: $dataPath" -ForegroundColor Green
                }
                catch {
                    if ($Force) {
                        # Use robocopy to remove stubborn files
                        $tempPath = "$env:TEMP\empty_$(Get-Random)"
                        New-Item -Path $tempPath -ItemType Directory -Force | Out-Null
                        
                        & robocopy $tempPath $dataPath /MIR /R:0 /W:0 2>&1 | Out-Null
                        Remove-Item -Path $tempPath -Force
                        Remove-Item -Path $dataPath -Force -ErrorAction SilentlyContinue
                        
                        Write-ColoredOutput "Data forcefully removed: $dataPath" -ForegroundColor Yellow
                    } else {
                        Write-ColoredOutput "WARNING: Could not remove all data files. Some files may be in use." -ForegroundColor Red
                        Write-ColoredOutput "Use -Force to override or manually delete: $dataPath" -ForegroundColor Yellow
                    }
                }
            }
        }
    } else {
        Write-ColoredOutput "Service data preserved at: C:\ProgramData\MigrationTool" -ForegroundColor Cyan
    }
    
    # Clean up registry entries
    Write-ColoredOutput "Cleaning registry entries..." -ForegroundColor Yellow
    
    $regPaths = @(
        "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName",
        "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$ServiceName"
    )
    
    foreach ($regPath in $regPaths) {
        if (Test-Path $regPath) {
            Remove-Item -Path $regPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    
    Write-ColoredOutput "Registry cleaned" -ForegroundColor Green
    
    # Display final status
    Write-Host ""
    Write-ColoredOutput "Uninstallation completed successfully!" -ForegroundColor Green
    Write-Host ""
    
    if (-not $RemoveData) {
        Write-ColoredOutput "Note: Service data was preserved. To remove it, run:" -ForegroundColor Yellow
        Write-Host "  .\Uninstall-Service.ps1 -RemoveData"
        Write-Host ""
    }
}
catch {
    Write-ColoredOutput "ERROR: $_" -ForegroundColor Red
    exit 1
}