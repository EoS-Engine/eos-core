using EOS.KnowledgeGraph;

namespace EOS.Knowledge.Tests;

public class ArchivedContentStoreTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("EOS_SQLSERVER_CONNECTION_STRING")
        ?? throw new InvalidOperationException("EOS_SQLSERVER_CONNECTION_STRING is not set.");

    [Fact]
    public async Task ArchiveAsync_ThenGetArchivedContentAsync_RoundTripsTheOriginalContent()
    {
        var store = new ArchivedContentStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);
        var sourceNodeId = Guid.NewGuid();
        var originalContent = $"original content {Guid.NewGuid()}";

        var archiveId = await store.ArchiveAsync(sourceNodeId, originalContent, CancellationToken.None);
        var retrieved = await store.GetArchivedContentAsync(archiveId, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, archiveId);
        Assert.Equal(originalContent, retrieved);
    }

    [Fact]
    public async Task GetArchivedContentAsync_ReturnsNull_ForAnUnknownArchiveId()
    {
        var store = new ArchivedContentStore(ConnectionString);
        await store.EnsureTableExistsAsync(CancellationToken.None);

        var result = await store.GetArchivedContentAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }
}
