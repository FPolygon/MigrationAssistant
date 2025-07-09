using Xunit;

namespace MigrationService.Tests;

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

public class DatabaseTestFixture : IDisposable
{
    private static readonly SemaphoreSlim DatabaseSemaphore = new(1, 1);

    public DatabaseTestFixture()
    {
        DatabaseSemaphore.Wait();
    }

    public void Dispose()
    {
        DatabaseSemaphore.Release();
    }
}
