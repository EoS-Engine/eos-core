using System.Net.Http.Json;
using System.Text.Json;
using EOS.Infrastructure;

namespace EOS.Infrastructure.Tests;

public class ChromaDbConnectivityTests
{
    private readonly DataStoreConnectionOptions _connectionOptions;

    public ChromaDbConnectivityTests()
    {
        EnvFileLoader.EnsureLoaded();
        _connectionOptions = DataStoreConnectionOptions.FromEnvironment();
    }

    [Fact]
    public async Task CheckChromaDbAsync_SucceedsAgainstTheRunningContainer()
    {
        var checker = new DataStoreHealthChecker(_connectionOptions, Path.GetTempPath());

        var result = await checker.CheckChromaDbAsync(CancellationToken.None);

        Assert.True(result.Healthy, result.Error);
    }

    [Fact]
    public async Task CheckChromaDbAsync_ReturnsUnhealthy_ForAnUnreachableEndpoint()
    {
        var invalidOptions = _connectionOptions with { ChromaDbEndpoint = "http://localhost:1" };
        var checker = new DataStoreHealthChecker(invalidOptions, Path.GetTempPath());

        var result = await checker.CheckChromaDbAsync(CancellationToken.None);

        Assert.False(result.Healthy);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SmokeTestCollection_CanBeCreatedListedAndDeleted()
    {
        using var client = new HttpClient { BaseAddress = new Uri(_connectionOptions.ChromaDbEndpoint) };
        var tenantDatabasePrefix = "api/v2/tenants/default_tenant/databases/default_database";
        var collectionName = $"eos-wp004-smoke-test-{Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync($"{tenantDatabasePrefix}/collections", new { name = collectionName });
        createResponse.EnsureSuccessStatusCode();

        var listResponse = await client.GetAsync($"{tenantDatabasePrefix}/collections");
        listResponse.EnsureSuccessStatusCode();
        var collections = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var found = collections.EnumerateArray().Any(c => c.GetProperty("name").GetString() == collectionName);

        var deleteResponse = await client.DeleteAsync($"{tenantDatabasePrefix}/collections/{collectionName}");

        Assert.True(found, "Smoke-test collection was not found after creation.");
        Assert.True(deleteResponse.IsSuccessStatusCode);
    }
}
