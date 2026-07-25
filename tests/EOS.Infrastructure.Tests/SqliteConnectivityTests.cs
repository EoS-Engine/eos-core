using EOS.Infrastructure;
using Microsoft.Data.Sqlite;

namespace EOS.Infrastructure.Tests;

public class SqliteConnectivityTests
{
    private readonly DataStoreConnectionOptions _connectionOptions;

    public SqliteConnectivityTests()
    {
        EnvFileLoader.EnsureLoaded();
        _connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    }

    [Fact]
    public async Task CheckSqliteAsync_SucceedsAgainstALocalDirectory()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("eos-sqlite-tests-").FullName;
        try
        {
            var checker = new DataStoreHealthChecker(_connectionOptions, dataDirectory);

            var result = await checker.CheckSqliteAsync(CancellationToken.None);

            Assert.True(result.Healthy, result.Error);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CheckSqliteAsync_ReturnsUnhealthy_WhenDataDirectoryIsNotUsable()
    {
        // A path nested under a file (not a directory) cannot be created, forcing a real failure.
        var blockingFile = Path.Combine(Path.GetTempPath(), $"eos-sqlite-blocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blockingFile, "not a directory");
        try
        {
            var uncreatableDirectory = Path.Combine(blockingFile, "data");
            var checker = new DataStoreHealthChecker(_connectionOptions, uncreatableDirectory);

            var result = await checker.CheckSqliteAsync(CancellationToken.None);

            Assert.False(result.Healthy);
            Assert.NotNull(result.Error);
        }
        finally
        {
            File.Delete(blockingFile);
        }
    }

    [Fact]
    public void TempTable_RoundTripsAWriteAndRead()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("eos-sqlite-tests-").FullName;
        try
        {
            var dbPath = Path.Combine(dataDirectory, "roundtrip.db");
            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using (var create = connection.CreateCommand())
            {
                create.CommandText = "CREATE TABLE SmokeTest (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)";
                create.ExecuteNonQuery();
            }

            using (var insert = connection.CreateCommand())
            {
                insert.CommandText = "INSERT INTO SmokeTest (Value) VALUES ('hello')";
                insert.ExecuteNonQuery();
            }

            using var select = connection.CreateCommand();
            select.CommandText = "SELECT Value FROM SmokeTest WHERE Id = 1";
            var value = (string?)select.ExecuteScalar();

            using (var drop = connection.CreateCommand())
            {
                drop.CommandText = "DROP TABLE SmokeTest";
                drop.ExecuteNonQuery();
            }

            Assert.Equal("hello", value);
        }
        finally
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
    }
}
