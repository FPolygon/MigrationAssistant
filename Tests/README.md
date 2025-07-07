# Migration Assistant Tests

**Current Status**: Phase 1 test infrastructure is complete with unit and integration tests for core service components. Current test coverage is 53.2% with a target of 70%.

## Test Structure

### Current Test Projects
- **MigrationService.Tests**: Unit and integration tests for the Windows service

### Future Test Projects (Planned)
- **MigrationAgent.Tests**: Tests for user notification agent (Phase 4)
- **MigrationBackup.Tests**: Tests for backup providers (Phase 5-6)
- **MigrationRestore.Tests**: Tests for restore wizard (Phase 9)

## Test Categories

Tests are organized into categories to support different execution environments:

### Unit Tests
- Standard unit tests with mocked dependencies
- Run in all environments including CI
- No external dependencies or system resources required

### Integration Tests
Tests marked with `[Trait("Category", "Integration")]` that:
- Use actual system resources (named pipes, file system, etc.)
- May require specific Windows permissions
- Are excluded from CI builds due to environment limitations

## Running Tests

### Run all tests locally:
```powershell
dotnet test
```

### Run only unit tests (CI-safe):
```powershell
dotnet test --filter "Category!=Integration&Category!=RequiresNamedPipes"
```

### Run only integration tests:
```powershell
dotnet test --filter "Category=Integration|Category=RequiresNamedPipes"
```

### Run with coverage:
```powershell
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

## Known Issues

### Named Pipes in CI
Integration tests using Windows named pipes may fail in GitHub Actions due to:
- Security context differences
- Permission restrictions in containerized environments  
- Resource cleanup issues with parallel test execution

These tests are configured to run in a separate CI job with `continue-on-error: true` to prevent build failures.

### Test Timeouts
- Unit tests: Should complete within seconds
- Integration tests: Have a 60-second timeout per test
- CI workflow: Has a 10-minute timeout for unit tests, 5-minute for integration tests

## Test Coverage

### Current Coverage: 53.2%
Target coverage for Phase 1: 70%

### Priority Areas for Coverage Improvement
1. **IPC subsystem**: Message handlers need tests
   - AgentStartedHandler
   - BackupProgressHandler  
   - DelayRequestHandler
2. **IPC client components**: 
   - IpcClient
   - ReconnectingIpcClient
3. **Database migration runner**
4. **Logging providers**:
   - ConsoleLogProvider
   - EventLogProvider
   - BufferedLogProvider

## Adding New Tests

When adding tests that use system resources:
1. Add appropriate category traits: `[Trait("Category", "Integration")]`
2. Ensure proper resource cleanup in test disposal
3. Use unique resource names (e.g., `$"TestPipe_{Guid.NewGuid()}"`)
4. Consider if the test will work in CI environments
5. Follow the xUnit collection isolation pattern for integration tests

### Test Collections

The project uses xUnit test collections to manage test isolation:
- **IpcIntegrationCollection**: For tests that use named pipes
- Additional collections can be added in `TestCollections.cs` as needed