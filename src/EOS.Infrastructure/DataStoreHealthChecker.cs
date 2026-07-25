using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using StackExchange.Redis;

namespace EOS.Infrastructure;

public sealed class DataStoreHealthChecker(DataStoreConnectionOptions connectionOptions, string sqliteDataDirectory)
{
    private static readonly HttpClient HttpClient = new();

    public async Task<IReadOnlyList<StoreHealthResult>> CheckAllAsync(CancellationToken cancellationToken)
    {
        return
        [
            await CheckSqlServerAsync(cancellationToken),
            await CheckRedisAsync(cancellationToken),
            await CheckChromaDbAsync(cancellationToken),
            await CheckSqliteAsync(cancellationToken),
        ];
    }

    public async Task<StoreHealthResult> CheckSqlServerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionOptions.SqlServerConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return new StoreHealthResult("SQL Server", true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StoreHealthResult("SQL Server", false, ex.Message);
        }
    }

    public async Task<StoreHealthResult> CheckRedisAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await ConnectionMultiplexer.ConnectAsync(connectionOptions.RedisConnectionString);
            await connection.GetDatabase().PingAsync();

            return new StoreHealthResult("Redis", true, null);
        }
        catch (Exception ex)
        {
            return new StoreHealthResult("Redis", false, ex.Message);
        }
    }

    public async Task<StoreHealthResult> CheckChromaDbAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await HttpClient.GetAsync(
                new Uri(new Uri(connectionOptions.ChromaDbEndpoint), "/api/v2/heartbeat"),
                cancellationToken);
            response.EnsureSuccessStatusCode();

            return new StoreHealthResult("ChromaDB", true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new StoreHealthResult("ChromaDB", false, ex.Message);
        }
    }

    public Task<StoreHealthResult> CheckSqliteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var resolvedDataDirectory = ExpandLeadingTilde(sqliteDataDirectory);
            Directory.CreateDirectory(resolvedDataDirectory);
            var dbPath = Path.Combine(resolvedDataDirectory, "eos-health-check.db");

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA schema_version";
            command.ExecuteScalar();

            return Task.FromResult(new StoreHealthResult("SQLite", true, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new StoreHealthResult("SQLite", false, ex.Message));
        }
    }

    private static string ExpandLeadingTilde(string path)
    {
        if (!path.StartsWith('~'))
        {
            return path;
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(homeDirectory, path.TrimStart('~', '/'));
    }
}
