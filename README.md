# Migration Assistant

[![CI](https://github.com/YOUR_USERNAME/MigrationAssistant/actions/workflows/ci.yml/badge.svg)](https://github.com/YOUR_USERNAME/MigrationAssistant/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/YOUR_USERNAME/MigrationAssistant/branch/main/graph/badge.svg)](https://codecov.io/gh/YOUR_USERNAME/MigrationAssistant)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A comprehensive Windows migration tool that facilitates the transition of domain-joined, SCCM-managed computers to workgroup Entra-joined, Intune-managed devices. The tool ensures all user data is safely backed up to OneDrive before performing a system reset for Autopilot enrollment.

## Overview

This migration tool is designed to:
- Run as a Windows service deployed via SCCM with SYSTEM privileges
- Detect and manage multiple user profiles on shared computers
- Backup user data, application settings, and system configurations to OneDrive
- Block system reset until all active users have completed their backups
- Provide a post-reset restore wizard to help users recover their data

## Key Features

### Multi-User Support
- Detects all user profiles on the system
- Identifies active users (last login <30 days, profile >100MB)
- Blocks migration until all active users complete backup
- Provides manual override for inactive profiles

### Smart Notification System
- Non-intrusive but persistent notifications
- Avoids interrupting during meetings or fullscreen applications
- Detects camera/microphone usage
- Integrates with calendar systems
- Allows limited delays before escalation

### Comprehensive Backup
- **User Files**: Documents, Desktop, Pictures, Downloads, AppData
- **Browsers**: Bookmarks, passwords, extensions, sync accounts
- **Email/Outlook**: All versions, settings, signatures, rules, PST files
- **System Config**: WiFi profiles, credentials, network drives, printers

### IT Integration
- Automatic escalation for issues
- Detailed error reporting
- OneDrive quota management
- Large file handling (PST >5GB)

### Post-Reset Recovery
- Standalone restore wizard
- Category-based restoration
- Priority-ordered recovery (WiFi → Browsers → Email → Files)
- Clear status reporting

## Project Status

**Current Phase**: Phase 1 - Core Service Implementation
- ✅ Windows Service framework
- ✅ SQLite state management  
- ✅ Named pipe IPC framework
- ✅ Unit test infrastructure
- 🚧 User profile detection logic
- ⏳ Agent UI implementation
- ⏳ Backup providers
- ⏳ Restore wizard

## System Requirements

### Platform Support
**This tool is designed exclusively for Windows x86_64 (64-bit) systems.** No other platforms or architectures are supported.

### Software Requirements
- Windows 10/11 (x64)
- .NET 8.0 SDK (for development)
- .NET Framework 4.8 (runtime)
- PowerShell 5.1+
- OneDrive for Business
- SCCM for deployment
- Visual Studio 2022 or VS Code (for development)
- Administrator privileges (for service installation)

## Building from Source

### Build Steps

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/MigrationAssistant.git
cd MigrationAssistant

# Build the solution
dotnet build MigrationAssistant.sln

# Run tests
dotnet test MigrationAssistant.sln

# Build for release
dotnet build MigrationAssistant.sln -c Release

# Or use the build script
./build.ps1 -Configuration Release -Test -Coverage
```

## Installation

### Using PowerShell Deployment Script

```powershell
# Install the migration tool
powershell.exe -ExecutionPolicy Bypass -File ".\PowerShell\Deploy-Migration.ps1" -Action Install -LogPath "C:\Windows\Logs\MigrationAssistant"

# Uninstall
powershell.exe -ExecutionPolicy Bypass -File ".\PowerShell\Deploy-Migration.ps1" -Action Uninstall

# Repair installation
powershell.exe -ExecutionPolicy Bypass -File ".\PowerShell\Deploy-Migration.ps1" -Action Repair
```

## Development

### Running Tests

```bash
# Run all tests with coverage
dotnet test MigrationAssistant.sln --collect:"XPlat Code Coverage"

# Run specific test project
dotnet test Tests/Unit/MigrationService.Tests/MigrationService.Tests.csproj

# Run with detailed output
dotnet test MigrationAssistant.sln --logger "console;verbosity=detailed"
```

### Service Management

```powershell
# Check service status
Get-Service MigrationService

# View service logs
Get-Content "C:\ProgramData\MigrationAssistant\Logs\*.log" -Tail 50

# Check agent scheduled task
Get-ScheduledTask -TaskName "MigrationAgent"
```

## Architecture

The Migration Assistant consists of several components:

1. **Migration Service** (Windows Service)
   - Runs as SYSTEM
   - Orchestrates the migration process
   - Manages state across all users
   - Enforces multi-user blocking

2. **Migration Agent** (User Application)
   - Runs in user context
   - Shows notifications and UI
   - Handles user interactions
   - Reports progress to service

3. **Backup Engine**
   - Provider-based architecture
   - Supports files, browsers, email, system settings
   - Uploads to OneDrive

4. **Restore Wizard**
   - Standalone executable
   - Stored in OneDrive
   - Runs post-reset to restore data

## Contributing

Please read [CONTRIBUTING.md](CONTRIBUTING.md) for details on our code of conduct and the process for submitting pull requests.

## Project Structure

```
MigrationAssistant/
├── src/                    # Source code
│   ├── MigrationService/   # Windows service
│   ├── MigrationAgent/     # User notification agent
│   ├── MigrationBackup/    # Backup engine library
│   ├── MigrationRestore/   # Restore wizard
│   └── Common/            # Shared components
├── PowerShell/            # PowerShell scripts
├── SCCM/                  # SCCM package files
├── Tests/                 # Unit and integration tests
└── docs/                  # Documentation
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md) - Detailed system design and component interaction
- [Implementation Plan](docs/IMPLEMENTATION_PLAN.md) - Phased development approach
- [API Design](docs/API_DESIGN.md) - Service interfaces and contracts
- [Deployment Guide](docs/DEPLOYMENT_GUIDE.md) - SCCM package creation and deployment

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

For issues and feature requests, please use the [GitHub issue tracker](https://github.com/YOUR_USERNAME/MigrationAssistant/issues).