namespace EOS.Infrastructure;

public sealed record DataStoreConnectionOptions(
    string SqlServerConnectionString,
    string RedisConnectionString,
    string ChromaDbEndpoint)
{
    public static DataStoreConnectionOptions FromEnvironment()
    {
        return new DataStoreConnectionOptions(
            SqlServerConnectionString: RequireEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING"),
            RedisConnectionString: RequireEnvironmentVariable("EOS_REDIS_CONNECTION_STRING"),
            ChromaDbEndpoint: RequireEnvironmentVariable("EOS_CHROMADB_ENDPOINT"));
    }

    private static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
    }
}
