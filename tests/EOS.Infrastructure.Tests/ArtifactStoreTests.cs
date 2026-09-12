using EOS.Contracts;
using EOS.Infrastructure;
using Microsoft.Data.SqlClient;

namespace EOS.Infrastructure.Tests;

// Post-Roadmap WP-A: Constitution Part 8 Artifact Registry semantics, against the real SQL Server.
public class ArtifactStoreTests
{
    private readonly ArtifactStore _store;
    private readonly string _connectionString;

    public ArtifactStoreTests()
    {
        EnvFileLoader.EnsureLoaded();
        _connectionString = DataStoreConnectionOptions.FromEnvironment().SqlServerConnectionString;
        _store = new ArtifactStore(_connectionString);
    }

    private static string UniqueContent(string label) => $"{label} {Guid.NewGuid():N}\n";

    [Fact]
    public void ComputeContentHash_MatchesTheKnownSha256Vector_ForExactUtf8Bytes()
    {
        // SHA-256("abc") — FIPS 180-4 test vector.
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ArtifactStore.ComputeContentHash("abc"));

        // No normalization: line endings are part of the identity.
        Assert.NotEqual(ArtifactStore.ComputeContentHash("a\nb"), ArtifactStore.ComputeContentHash("a\r\nb"));
    }

    [Fact]
    public async Task RegisterAsync_ThenGetByHashAsync_RoundTripsTheArtifact()
    {
        await _store.EnsureTableExistsAsync(CancellationToken.None);
        var content = UniqueContent("round trip");

        var registered = await _store.RegisterAsync("Evidence", "EOS.SeniorEngineer", content);
        var readBack = await _store.GetByHashAsync(registered.ContentHash);

        Assert.NotNull(readBack);
        Assert.Equal(ArtifactStore.ComputeContentHash(content), registered.ContentHash);
        Assert.Equal(registered.ContentHash, readBack.ContentHash);
        Assert.Equal("Evidence", readBack.Type);
        Assert.Equal("EOS.SeniorEngineer", readBack.Producer);
        Assert.Equal(content, readBack.Content);
        Assert.Null(readBack.PreviousVersionHash);
    }

    [Fact]
    public async Task RegisterAsync_IsIdempotent_ForIdenticalContent_AndKeepsTheFirstRegistrationsMetadata()
    {
        await _store.EnsureTableExistsAsync(CancellationToken.None);
        var content = UniqueContent("duplicate");

        var first = await _store.RegisterAsync("Evidence", "first-producer", content);
        var second = await _store.RegisterAsync("Report", "second-producer", content);

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal("Evidence", second.Type);
        Assert.Equal("first-producer", second.Producer);
        Assert.Equal(1, await CountRowsAsync(first.ContentHash));
    }

    [Fact]
    public async Task RegisterAsync_LinksToAnExistingPreviousVersion()
    {
        await _store.EnsureTableExistsAsync(CancellationToken.None);
        var v1 = await _store.RegisterAsync("Evidence", "EOS.SeniorEngineer", UniqueContent("v1"));

        var v2 = await _store.RegisterAsync("Evidence", "EOS.SeniorEngineer", UniqueContent("v2"), previousVersionHash: v1.ContentHash);
        var readBack = await _store.GetByHashAsync(v2.ContentHash);

        Assert.Equal(v1.ContentHash, readBack!.PreviousVersionHash);
    }

    [Fact]
    public async Task RegisterAsync_Throws_WhenPreviousVersionHashDoesNotResolve()
    {
        await _store.EnsureTableExistsAsync(CancellationToken.None);
        var unregistered = ArtifactStore.ComputeContentHash(UniqueContent("never registered"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.RegisterAsync("Evidence", "EOS.SeniorEngineer", UniqueContent("orphan"), previousVersionHash: unregistered));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015a")]
    public async Task GetByHashAsync_Throws_ForAMalformedHash(string malformed)
    {
        await _store.EnsureTableExistsAsync(CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => _store.GetByHashAsync(malformed));
    }

    [Fact]
    public async Task GetByHashAsync_ReturnsNull_ForAnUnregisteredHash()
    {
        await _store.EnsureTableExistsAsync(CancellationToken.None);

        var result = await _store.GetByHashAsync(ArtifactStore.ComputeContentHash(UniqueContent("missing")));

        Assert.Null(result);
    }

    [Fact]
    public async Task EnsureTableExistsAsync_IsIdempotent()
    {
        await _store.EnsureTableExistsAsync(CancellationToken.None);
        await _store.EnsureTableExistsAsync(CancellationToken.None);

        Assert.NotNull(_store as IArtifactRegistryClient);
    }

    private async Task<int> CountRowsAsync(string contentHash)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Artifact WHERE ContentHash = @ContentHash";
        command.Parameters.AddWithValue("@ContentHash", contentHash);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
