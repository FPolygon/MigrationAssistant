# Windows Migration Tool

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

## Architecture

The solution consists of four main components:

1. **MigrationService.exe** - Windows service that orchestrates the migration
2. **MigrationAgent.exe** - Per-user notification and interaction agent
3. **MigrationBackup.dll** - Modular backup engine
4. **MigrationRestore.exe** - Post-reset restoration wizard

## Requirements

- Windows 10/11
- PowerShell 5.1+
- .NET Framework 4.8
- OneDrive for Business
- SCCM for deployment

## Documentation

- [Architecture](docs/ARCHITECTURE.md) - Detailed system design and component interaction
- [Implementation Plan](docs/IMPLEMENTATION_PLAN.md) - Phased development approach
- [API Design](docs/API_DESIGN.md) - Service interfaces and contracts
- [Deployment Guide](docs/DEPLOYMENT_GUIDE.md) - SCCM package creation and deployment

## Project Structure

```
MigrationTool/
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

## Getting Started

1. Review the [Architecture](docs/ARCHITECTURE.md) document
2. Follow the [Implementation Plan](docs/IMPLEMENTATION_PLAN.md)
3. See [Deployment Guide](docs/DEPLOYMENT_GUIDE.md) for SCCM setup

## Development Status

This project is in the planning phase. Implementation will proceed in phases:
1. Core service framework
2. User detection and OneDrive integration
3. Notification system
4. Backup modules
5. IT escalation
6. Restore wizard

## License

[To be determined]

## Contact

[IT Department Contact Information]