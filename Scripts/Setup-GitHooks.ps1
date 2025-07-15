#Requires -Version 5.1
<#
.SYNOPSIS
    Sets up Git hooks for the MigrationAssistant project.

.DESCRIPTION
    This script configures Git to use the project's custom hooks directory
    and ensures all hooks are properly installed and executable.

.EXAMPLE
    .\Scripts\Setup-GitHooks.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Write-Host "Setting up Git hooks for MigrationAssistant..." -ForegroundColor Cyan

# Get the repository root
$repoRoot = git rev-parse --show-toplevel 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Error "This script must be run from within the Git repository."
    exit 1
}

# Set the Git hooks path
Write-Host "Configuring Git to use .githooks directory..."
git config core.hooksPath .githooks

if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ Git hooks configured successfully!" -ForegroundColor Green
    Write-Host ""
    Write-Host "The following hooks are now active:" -ForegroundColor Yellow
    Get-ChildItem -Path "$repoRoot/.githooks" -File | ForEach-Object {
        Write-Host "  - $($_.Name)" -ForegroundColor Gray
    }
    Write-Host ""
    Write-Host "Pre-commit hook will automatically check code formatting before each commit." -ForegroundColor Cyan
} else {
    Write-Error "Failed to configure Git hooks."
    exit 1
}

# Verify dotnet format is available
Write-Host "Verifying dotnet format tool..."
$formatCheck = dotnet format --version 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "✅ dotnet format is available (version: $formatCheck)" -ForegroundColor Green
} else {
    Write-Warning "dotnet format tool not found. Installing..."
    dotnet tool install -g dotnet-format
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ dotnet format installed successfully!" -ForegroundColor Green
    } else {
        Write-Error "Failed to install dotnet format. Please install manually."
    }
}

Write-Host ""
Write-Host "Setup complete! 🎉" -ForegroundColor Green
Write-Host ""
Write-Host "To test the pre-commit hook, try making a commit with formatting issues." -ForegroundColor Cyan
Write-Host "To bypass the hook (not recommended), use: git commit --no-verify" -ForegroundColor Gray