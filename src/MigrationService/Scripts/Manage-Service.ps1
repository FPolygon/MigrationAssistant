#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Manages the Migration Service operations and troubleshooting.

.DESCRIPTION
    This script provides various management functions for the Migration Service including
    status checks, log viewing, troubleshooting, and configuration updates.

.PARAMETER Action
    The action to perform. Valid values are:
    - Status: Show detailed service status
    - Logs: View recent service logs
    - Test: Run service connectivity tests
    - Restart: Restart the service
    - Export: Export service configuration and logs for support

.PARAMETER LogLines
    Number of log lines to display (default: 50)

.EXAMPLE
    .\Manage-Service.ps1 -Action Status
    Shows detailed service status information.

.EXAMPLE
    .\Manage-Service.ps1 -Action Logs -LogLines 100
    Shows the last 100 lines of service logs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Status', 'Logs', 'Test', 'Restart', 'Export')]
    [string]$Action,
    
    [Parameter()]
    [int]$LogLines = 50
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

function Get-ServiceDetailedStatus {
    try {
        $service = Get-Service -Name $ServiceName -ErrorAction Stop
        $process = Get-Process -Name "MigrationService" -ErrorAction SilentlyContinue
        
        Write-ColoredOutput "Migration Service Status" -ForegroundColor Cyan
        Write-ColoredOutput "======================" -ForegroundColor Cyan
        Write-Host ""
        
        # Basic service info
        Write-Host "Service Information:"
        Write-Host "  Name:          $($service.Name)"
        Write-Host "  Display Name:  $($service.DisplayName)"
        Write-Host "  Status:        $($service.Status)"
        Write-Host "  Start Type:    $($service.StartType)"
        Write-Host ""
        
        # Process information if running
        if ($process) {
            Write-Host "Process Information:"
            Write-Host "  Process ID:    $($process.Id)"
            Write-Host "  Start Time:    $($process.StartTime)"
            Write-Host "  CPU Time:      $($process.TotalProcessorTime)"
            Write-Host "  Memory (MB):   $([math]::Round($process.WorkingSet64 / 1MB, 2))"
            Write-Host "  Threads:       $($process.Threads.Count)"
            Write-Host ""
        }
        
        # Check key directories
        Write-Host "Directory Status:"
        $directories = @{
            'Data'    = 'C:\ProgramData\MigrationTool\Data'
            'Logs'    = 'C:\ProgramData\MigrationTool\Logs'
            'Backups' = 'C:\ProgramData\MigrationTool\Backups'
        }
        
        foreach ($dir in $directories.GetEnumerator()) {
            if (Test-Path $dir.Value) {
                $items = Get-ChildItem $dir.Value -ErrorAction SilentlyContinue
                Write-Host "  $($dir.Key): Exists ($($items.Count) items)"
            } else {
                Write-Host "  $($dir.Key): Not found" -ForegroundColor Yellow
            }
        }
        Write-Host ""
        
        # Check database
        $dbPath = 'C:\ProgramData\MigrationTool\Data\migration.db'
        if (Test-Path $dbPath) {
            $dbInfo = Get-Item $dbPath
            Write-Host "Database Information:"
            Write-Host "  Path:          $dbPath"
            Write-Host "  Size (MB):     $([math]::Round($dbInfo.Length / 1MB, 2))"
            Write-Host "  Last Modified: $($dbInfo.LastWriteTime)"
            Write-Host ""
        }
        
        # Recent events
        Write-Host "Recent Windows Events:"
        try {
            $events = Get-EventLog -LogName Application -Source $ServiceName -Newest 5 -ErrorAction SilentlyContinue
            if ($events) {
                foreach ($event in $events) {
                    $color = switch ($event.EntryType) {
                        'Error'   { 'Red' }
                        'Warning' { 'Yellow' }
                        default   { 'Gray' }
                    }
                    Write-ColoredOutput "  [$($event.TimeGenerated)] $($event.EntryType): $($event.Message.Split("`n")[0])" -ForegroundColor $color
                }
            } else {
                Write-Host "  No recent events found"
            }
        }
        catch {
            Write-Host "  Unable to read event log"
        }
    }
    catch {
        Write-ColoredOutput "ERROR: Service '$ServiceName' not found" -ForegroundColor Red
    }
}

function Show-ServiceLogs {
    param([int]$Lines)
    
    $logPath = 'C:\ProgramData\MigrationTool\Logs'
    
    if (-not (Test-Path $logPath)) {
        Write-ColoredOutput "Log directory not found: $logPath" -ForegroundColor Red
        return
    }
    
    # Find the most recent log file
    $logFiles = Get-ChildItem -Path $logPath -Filter "service-*.txt" | Sort-Object LastWriteTime -Descending
    
    if ($logFiles.Count -eq 0) {
        Write-ColoredOutput "No log files found" -ForegroundColor Yellow
        return
    }
    
    $latestLog = $logFiles[0]
    
    Write-ColoredOutput "Showing last $Lines lines from: $($latestLog.Name)" -ForegroundColor Cyan
    Write-ColoredOutput "============================================" -ForegroundColor Cyan
    Write-Host ""
    
    Get-Content $latestLog.FullName -Tail $Lines | ForEach-Object {
        if ($_ -match '\[ERR\]|\[ERROR\]') {
            Write-ColoredOutput $_ -ForegroundColor Red
        }
        elseif ($_ -match '\[WRN\]|\[WARNING\]') {
            Write-ColoredOutput $_ -ForegroundColor Yellow
        }
        elseif ($_ -match '\[DBG\]|\[DEBUG\]') {
            Write-ColoredOutput $_ -ForegroundColor DarkGray
        }
        else {
            Write-Host $_
        }
    }
}

function Test-ServiceConnectivity {
    Write-ColoredOutput "Running Service Connectivity Tests" -ForegroundColor Cyan
    Write-ColoredOutput "=================================" -ForegroundColor Cyan
    Write-Host ""
    
    # Test 1: Service is running
    Write-Host "Test 1: Service Status"
    $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($service -and $service.Status -eq 'Running') {
        Write-ColoredOutput "  PASS: Service is running" -ForegroundColor Green
    } else {
        Write-ColoredOutput "  FAIL: Service is not running" -ForegroundColor Red
    }
    
    # Test 2: Named pipe exists
    Write-Host ""
    Write-Host "Test 2: Named Pipe"
    $pipeName = "MigrationService_$env:COMPUTERNAME"
    $pipeExists = [System.IO.Directory]::GetFiles("\\.\pipe\") | Where-Object { $_ -match $pipeName }
    
    if ($pipeExists) {
        Write-ColoredOutput "  PASS: Named pipe exists: $pipeName" -ForegroundColor Green
    } else {
        Write-ColoredOutput "  FAIL: Named pipe not found: $pipeName" -ForegroundColor Red
    }
    
    # Test 3: Database connectivity
    Write-Host ""
    Write-Host "Test 3: Database"
    $dbPath = 'C:\ProgramData\MigrationTool\Data\migration.db'
    if (Test-Path $dbPath) {
        try {
            # Try to open the database file (read-only)
            $fs = [System.IO.File]::Open($dbPath, 'Open', 'Read', 'Read')
            $fs.Close()
            Write-ColoredOutput "  PASS: Database is accessible" -ForegroundColor Green
        }
        catch {
            Write-ColoredOutput "  FAIL: Database is locked or inaccessible" -ForegroundColor Red
        }
    } else {
        Write-ColoredOutput "  FAIL: Database not found" -ForegroundColor Red
    }
    
    # Test 4: Event log
    Write-Host ""
    Write-Host "Test 4: Event Log"
    try {
        $eventLog = Get-EventLog -List | Where-Object { $_.Log -eq 'Application' }
        if ($eventLog) {
            Write-ColoredOutput "  PASS: Event log is accessible" -ForegroundColor Green
        }
    }
    catch {
        Write-ColoredOutput "  FAIL: Cannot access event log" -ForegroundColor Red
    }
    
    # Test 5: Port availability (if applicable)
    Write-Host ""
    Write-Host "Test 5: System Resources"
    $process = Get-Process -Name "MigrationService" -ErrorAction SilentlyContinue
    if ($process) {
        $memoryMB = [math]::Round($process.WorkingSet64 / 1MB, 2)
        if ($memoryMB -lt 500) {
            Write-ColoredOutput "  PASS: Memory usage is normal ($memoryMB MB)" -ForegroundColor Green
        } else {
            Write-ColoredOutput "  WARN: High memory usage ($memoryMB MB)" -ForegroundColor Yellow
        }
    }
}

function Restart-MigrationService {
    Write-ColoredOutput "Restarting Migration Service..." -ForegroundColor Yellow
    
    try {
        # Stop the service
        Stop-Service -Name $ServiceName -Force
        Write-Host "Service stopped"
        
        # Wait a moment
        Start-Sleep -Seconds 2
        
        # Start the service
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
        
        Write-ColoredOutput "Service restarted successfully" -ForegroundColor Green
    }
    catch {
        Write-ColoredOutput "ERROR: $_" -ForegroundColor Red
    }
}

function Export-ServiceDiagnostics {
    $exportPath = "$env:TEMP\MigrationService_Diagnostics_$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    
    Write-ColoredOutput "Exporting service diagnostics..." -ForegroundColor Yellow
    Write-Host "Export path: $exportPath"
    Write-Host ""
    
    try {
        # Create export directory
        New-Item -Path $exportPath -ItemType Directory -Force | Out-Null
        
        # Export service status
        Write-Host "Exporting service status..."
        Get-Service -Name $ServiceName | Format-List * | Out-File "$exportPath\ServiceStatus.txt"
        
        # Export recent logs
        Write-Host "Exporting logs..."
        $logPath = 'C:\ProgramData\MigrationTool\Logs'
        if (Test-Path $logPath) {
            $recentLogs = Get-ChildItem -Path $logPath -Filter "*.txt" | 
                          Sort-Object LastWriteTime -Descending | 
                          Select-Object -First 5
            
            foreach ($log in $recentLogs) {
                Copy-Item $log.FullName -Destination $exportPath
            }
        }
        
        # Export event logs
        Write-Host "Exporting event logs..."
        try {
            Get-EventLog -LogName Application -Source $ServiceName -Newest 100 |
                Export-Csv "$exportPath\EventLog.csv" -NoTypeInformation
        }
        catch {
            "Unable to export event logs: $_" | Out-File "$exportPath\EventLog_Error.txt"
        }
        
        # Export system information
        Write-Host "Exporting system information..."
        @"
System Information Export
Generated: $(Get-Date)

Computer Name: $env:COMPUTERNAME
Windows Version: $([System.Environment]::OSVersion.VersionString)
.NET Version: $([System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription)

Service Executable: $(Get-Process -Name "MigrationService" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path)
"@ | Out-File "$exportPath\SystemInfo.txt"
        
        # Create ZIP file
        Write-Host "Creating archive..."
        $zipPath = "$exportPath.zip"
        Compress-Archive -Path "$exportPath\*" -DestinationPath $zipPath -Force
        
        # Clean up directory
        Remove-Item $exportPath -Recurse -Force
        
        Write-ColoredOutput "Diagnostics exported successfully!" -ForegroundColor Green
        Write-Host "File: $zipPath"
        Write-Host ""
        Write-ColoredOutput "Please attach this file when requesting support." -ForegroundColor Cyan
    }
    catch {
        Write-ColoredOutput "ERROR: $_" -ForegroundColor Red
    }
}

# Main execution
try {
    switch ($Action) {
        'Status' {
            Get-ServiceDetailedStatus
        }
        'Logs' {
            Show-ServiceLogs -Lines $LogLines
        }
        'Test' {
            Test-ServiceConnectivity
        }
        'Restart' {
            Restart-MigrationService
        }
        'Export' {
            Export-ServiceDiagnostics
        }
    }
}
catch {
    Write-ColoredOutput "ERROR: $_" -ForegroundColor Red
    exit 1
}