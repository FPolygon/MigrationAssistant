# Migration Assistant

> **Note**: GitHub repository URLs in badges below are placeholders and will be updated when the repository is published.

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

**Current Implementation**: Phase 1 (Core Service Framework), Phase 2 (User Detection and Profile Management), and Phase 3.1 (OneDrive Detection) are complete. The system can now detect user profiles, classify their activity, and identify OneDrive installations with sync status. User-facing features and backup functionality will be implemented in subsequent phases.

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

**Phase 1: Core Service Framework** ✅ COMPLETED
- ✅ Windows Service framework with lifecycle management
- ✅ SQLite state management with migration system
- ✅ Named pipe IPC framework with JSON protocol
- ✅ Structured logging with multiple providers
- ✅ PowerShell deployment and installation scripts
- ✅ Unit test infrastructure

**Phase 2: User Detection and Profile Management** ✅ COMPLETED
- ✅ Windows profile enumeration via registry
- ✅ Activity detection from multiple sources (event logs, registry, file system)
- ✅ Sophisticated activity scoring algorithm
- ✅ Rule-based profile classification engine
- ✅ Manual classification overrides with audit trail
- ✅ Database schema for profile tracking

**Phase 3: OneDrive Integration** 
- **Phase 3.1: OneDrive Detection** ✅ COMPLETED
  - ✅ OneDrive for Business installation detection
  - ✅ Account configuration and sync status monitoring
  - ✅ SharePoint library and sync folder enumeration
  - ✅ Known Folder Move (KFM) status detection
  - ✅ Authentication error detection and handling
  - ✅ 5-minute status caching for performance
  - ✅ State persistence via extended IStateManager
- **Phase 3.2: Sync Management** 📅 READY TO IMPLEMENT
- **Phase 3.3: Quota Management** 📅 READY TO IMPLEMENT

**Upcoming Phases**:
- 📅 Phase 4: Notification System (Agent UI)
- 📅 Phase 5-6: Backup System
- 📅 Phase 7: Multi-User Coordination
- 📅 Phase 8: IT Escalation System
- 📅 Phase 9: Restore Wizard
- 📅 Phase 10: Testing and Hardening

## System Requirements

### Platform Support
**This tool is designed exclusively for Windows x86_64 (64-bit) systems.** No other platforms or architectures are supported.

### Software Requirements
- Windows 10/11 (x64)
- .NET 8.0 SDK (for development)
- .NET 8.0 Desktop Runtime (for production)
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
./build.ps1 -Configuration Release -Runtime win-x64 -Test
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
├── src/                     # Source code
│   ├── MigrationService/    # Windows service ✅
│   │   ├── Core/           # Service management, state, IPC ✅
│   │   ├── Database/       # SQLite and migrations ✅
│   │   ├── IPC/            # Named pipe framework ✅
│   │   ├── Logging/        # Structured logging system ✅
│   │   ├── ProfileManagement/ # User detection and classification ✅
│   │   ├── OneDrive/       # OneDrive detection and management ✅
│   │   ├── Models/         # Data models and entities ✅
│   │   └── Scripts/        # Service management scripts ✅
│   ├── MigrationAgent/     # User notification agent (Phase 4) 📅
│   ├── MigrationBackup/    # Backup engine library (Phase 5-6) 📅
│   ├── MigrationRestore/   # Restore wizard (Phase 9) 📅
│   └── Common/             # Shared components 📅
├── PowerShell/             # Deployment scripts ✅
├── Tests/                  # Unit and integration tests ✅
│   └── Unit/
│       └── MigrationService.Tests/
├── docs/                   # Documentation ✅
│   ├── ARCHITECTURE.md
│   ├── API_DESIGN.md
│   ├── DEPLOYMENT_GUIDE.md
│   └── IMPLEMENTATION_PLAN.md
└── .github/                # GitHub Actions workflows ✅
```

## Documentation

- [Architecture](docs/ARCHITECTURE.md) - Detailed system design and component interaction
- [Implementation Plan](docs/IMPLEMENTATION_PLAN.md) - Phased development approach
- [API Design](docs/API_DESIGN.md) - Service interfaces and contracts
- [Deployment Guide](docs/DEPLOYMENT_GUIDE.md) - SCCM package creation and deployment

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

For issues and feature requests, please use the [GitHub issue tracker](https://github.com/YOUR_USERNAME/MigrationAssistant/issues) (URL to be updated when repository is published).