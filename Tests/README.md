# Migration Assistant Tests

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

## Adding New Tests

When adding tests that use system resources:
1. Add appropriate category traits: `[Trait("Category", "Integration")]`
2. Ensure proper resource cleanup in test disposal
3. Use unique resource names (e.g., `$"TestPipe_{Guid.NewGuid()}"`)
4. Consider if the test will work in CI environments