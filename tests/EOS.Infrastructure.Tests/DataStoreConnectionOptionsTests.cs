using EOS.Infrastructure;

namespace EOS.Infrastructure.Tests;

public class DataStoreConnectionOptionsTests : IDisposable
{
    private const string SqlServerVar = "EOS_SQLSERVER_CONNECTION_STRING";
    private const string RedisVar = "EOS_REDIS_CONNECTION_STRING";
    private const string ChromaDbVar = "EOS_CHROMADB_ENDPOINT";

    private readonly string? _originalSqlServerValue = Environment.GetEnvironmentVariable(SqlServerVar);
    private readonly string? _originalRedisValue = Environment.GetEnvironmentVariable(RedisVar);
    private readonly string? _originalChromaDbValue = Environment.GetEnvironmentVariable(ChromaDbVar);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SqlServerVar, _originalSqlServerValue);
        Environment.SetEnvironmentVariable(RedisVar, _originalRedisValue);
        Environment.SetEnvironmentVariable(ChromaDbVar, _originalChromaDbValue);
    }

    [Fact]
    public void FromEnvironment_ThrowsWhenARequiredVariableIsMissing()
    {
        Environment.SetEnvironmentVariable(SqlServerVar, "Server=localhost;");
        Environment.SetEnvironmentVariable(RedisVar, "localhost:6379");
        Environment.SetEnvironmentVariable(ChromaDbVar, null);

        var exception = Assert.Throws<InvalidOperationException>(() => DataStoreConnectionOptions.FromEnvironment());
        Assert.Contains(ChromaDbVar, exception.Message);
    }

    [Fact]
    public void FromEnvironment_SucceedsWhenAllRequiredVariablesArePresent()
    {
        Environment.SetEnvironmentVariable(SqlServerVar, "Server=localhost;Database=master;");
        Environment.SetEnvironmentVariable(RedisVar, "localhost:6379");
        Environment.SetEnvironmentVariable(ChromaDbVar, "http://localhost:8000");

        var options = DataStoreConnectionOptions.FromEnvironment();

        Assert.Equal("Server=localhost;Database=master;", options.SqlServerConnectionString);
        Assert.Equal("localhost:6379", options.RedisConnectionString);
        Assert.Equal("http://localhost:8000", options.ChromaDbEndpoint);
    }
}
