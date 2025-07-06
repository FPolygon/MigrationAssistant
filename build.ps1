#Requires -Version 5.1
<#
.SYNOPSIS
    Build script for Migration Assistant

.DESCRIPTION
    This script builds the Migration Assistant solution with various options for
    configuration, testing, and packaging.

.PARAMETER Configuration
    Build configuration (Debug or Release). Default is Debug.

.PARAMETER Test
    Run tests after building.

.PARAMETER Coverage
    Generate code coverage report (requires Test flag).

.PARAMETER Clean
    Clean the solution before building.

.PARAMETER Package
    Create deployment package after building.

.PARAMETER SkipRestore
    Skip NuGet package restore.

.EXAMPLE
    .\build.ps1 -Configuration Release -Test -Coverage
    Builds in Release mode, runs tests with coverage

.EXAMPLE
    .\build.ps1 -Clean -Package
    Cleans, builds in Debug mode, and creates package
#>

[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    
    [switch]$Test,
    
    [switch]$Coverage,
    
    [switch]$Clean,
    
    [switch]$Package,
    
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Script variables
$RootPath = $PSScriptRoot
$SolutionPath = Join-Path $RootPath "MigrationAssistant.sln"
$TestResultsPath = Join-Path $RootPath "TestResults"
$CoveragePath = Join-Path $RootPath "CoverageReport"
$PackagePath = Join-Path $RootPath "Package"

# Functions
function Write-Header {
    param([string]$Message)
    Write-Host "`n==== $Message ====" -ForegroundColor Cyan
}

function Test-DotNetCli {
    try {
        $version = dotnet --version
        Write-Host "Using .NET SDK version: $version" -ForegroundColor Green
        
        # Check if it's .NET 8.0 or higher
        if ($version -notmatch '^8\.' -and $version -notmatch '^9\.') {
            throw ".NET 8.0 SDK or higher is required. Current version: $version"
        }
    }
    catch {
        throw "dotnet CLI not found. Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download"
    }
}

function Clear-Solution {
    Write-Header "Cleaning solution"
    
    # Clean via dotnet
    & dotnet clean $SolutionPath --configuration $Configuration --verbosity minimal
    
    # Remove output directories
    Get-ChildItem -Path $RootPath -Include bin,obj -Recurse -Directory | 
        Where-Object { $_.Exists } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    
    # Remove test results
    if (Test-Path $TestResultsPath) {
        Remove-Item $TestResultsPath -Recurse -Force
    }
    
    # Remove coverage reports
    if (Test-Path $CoveragePath) {
        Remove-Item $CoveragePath -Recurse -Force
    }
    
    Write-Host "Clean completed" -ForegroundColor Green
}

function Restore-Package {
    if ($SkipRestore) {
        Write-Host "Skipping package restore" -ForegroundColor Yellow
        return
    }
    
    Write-Header "Restoring NuGet packages"
    & dotnet restore $SolutionPath --verbosity minimal
    
    if ($LASTEXITCODE -ne 0) {
        throw "Package restore failed"
    }
    
    Write-Host "Package restore completed" -ForegroundColor Green
}

function Invoke-Build {
    Write-Header "Building solution"
    Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
    
    $buildArgs = @(
        'build'
        $SolutionPath
        '--configuration', $Configuration
        '--no-restore'
        '--verbosity', 'minimal'
        '/p:TreatWarningsAsErrors=true'
        '/p:RunAnalyzersDuringBuild=true'
    )
    
    & dotnet @buildArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    
    Write-Host "Build completed successfully" -ForegroundColor Green
}

function Invoke-Test {
    Write-Header "Running tests"
    
    $testArgs = @(
        'test'
        $SolutionPath
        '--configuration', $Configuration
        '--no-build'
        '--verbosity', 'normal'
        '--logger', "trx;LogFileName=test-results-$Configuration.trx"
        '--logger', 'console;verbosity=detailed'
        '--results-directory', $TestResultsPath
    )
    
    if ($Coverage) {
        $testArgs += @(
            '--collect:"XPlat Code Coverage"'
            '--', 'DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover'
        )
    }
    
    & dotnet @testArgs
    
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed"
    }
    
    Write-Host "Tests completed successfully" -ForegroundColor Green
    
    if ($Coverage) {
        New-CoverageReport
    }
}

function New-CoverageReport {
    Write-Header "Generating coverage report"
    
    # Check if report generator is installed
    $reportGeneratorPath = & dotnet tool list -g | Select-String "dotnet-reportgenerator-globaltool"
    if (-not $reportGeneratorPath) {
        Write-Host "Installing ReportGenerator tool..." -ForegroundColor Yellow
        & dotnet tool install -g dotnet-reportgenerator-globaltool
    }
    
    # Find coverage files
    $coverageFiles = Get-ChildItem -Path $TestResultsPath -Filter "coverage.opencover.xml" -Recurse
    
    if ($coverageFiles.Count -eq 0) {
        Write-Warning "No coverage files found"
        return
    }
    
    $reports = ($coverageFiles | ForEach-Object { $_.FullName }) -join ';'
    
    # Generate report
    & reportgenerator `
        "-reports:$reports" `
        "-targetdir:$CoveragePath" `
        "-reporttypes:Html;Badges;TextSummary;Cobertura" `
        "-verbosity:Warning"
    
    # Display summary
    $summaryFile = Join-Path $CoveragePath "Summary.txt"
    if (Test-Path $summaryFile) {
        Write-Host "`nCoverage Summary:" -ForegroundColor Cyan
        Get-Content $summaryFile | Write-Host
    }
    
    Write-Host "`nCoverage report generated at: $CoveragePath\index.html" -ForegroundColor Green
}

function New-Package {
    Write-Header "Creating deployment package"
    
    # Create package directory
    if (Test-Path $PackagePath) {
        Remove-Item $PackagePath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PackagePath | Out-Null
    
    # Define package structure
    $packageBinPath = Join-Path $PackagePath "bin"
    $packageScriptsPath = Join-Path $PackagePath "scripts"
    $packageDocsPath = Join-Path $PackagePath "docs"
    
    # Create directories
    New-Item -ItemType Directory -Path $packageBinPath | Out-Null
    New-Item -ItemType Directory -Path $packageScriptsPath | Out-Null
    New-Item -ItemType Directory -Path $packageDocsPath | Out-Null
    
    # Copy service binaries
    $serviceBinPath = Join-Path $RootPath "src\MigrationService\bin\$Configuration\net8.0-windows"
    if (Test-Path $serviceBinPath) {
        Copy-Item -Path "$serviceBinPath\*" -Destination $packageBinPath -Recurse
    }
    
    # Copy PowerShell scripts
    $psScriptsPath = Join-Path $RootPath "PowerShell"
    if (Test-Path $psScriptsPath) {
        Copy-Item -Path "$psScriptsPath\*" -Destination $packageScriptsPath -Recurse
    }
    
    # Copy service scripts
    $serviceScriptsPath = Join-Path $RootPath "src\MigrationService\Scripts"
    if (Test-Path $serviceScriptsPath) {
        Copy-Item -Path "$serviceScriptsPath\*" -Destination $packageScriptsPath -Recurse
    }
    
    # Copy documentation
    $docsPath = Join-Path $RootPath "docs"
    if (Test-Path $docsPath) {
        Copy-Item -Path "$docsPath\*.md" -Destination $packageDocsPath
    }
    
    # Copy README
    $readmePath = Join-Path $RootPath "README.md"
    if (Test-Path $readmePath) {
        Copy-Item -Path $readmePath -Destination $PackagePath
    }
    
    # Create version file
    $version = "1.0.0.$($env:GITHUB_RUN_NUMBER ?? '0')"
    $versionInfo = @{
        Version = $version
        Configuration = $Configuration
        BuildDate = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        GitCommit = if (Get-Command git -ErrorAction SilentlyContinue) { & git rev-parse --short HEAD 2>$null } else { 'unknown' }
    }
    $versionInfo | ConvertTo-Json | Set-Content -Path (Join-Path $PackagePath "version.json")
    
    # Create ZIP package
    $zipPath = Join-Path $RootPath "MigrationAssistant-{0}-{1}.zip" -f $Configuration, $version
    Compress-Archive -Path "$PackagePath\*" -DestinationPath $zipPath -Force
    
    Write-Host "Package created: $zipPath" -ForegroundColor Green
}

# Main execution
try {
    Write-Host "Migration Assistant Build Script" -ForegroundColor Magenta
    Write-Host ("=" * 32) -ForegroundColor Magenta
    
    # Verify prerequisites
    Test-DotNetCli
    
    # Clean if requested
    if ($Clean) {
        Clear-Solution
    }
    
    # Restore packages
    Restore-Package
    
    # Build solution
    Invoke-Build
    
    # Run tests if requested
    if ($Test) {
        Invoke-Test
    }
    
    # Create package if requested
    if ($Package) {
        New-Package
    }
    
    Write-Host "`nBuild completed successfully!" -ForegroundColor Green
    exit 0
}
catch {
    Write-Error "Build failed: $_"
    exit 1
}