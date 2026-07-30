using System.Net.Http.Json;
using System.Text.Json;

namespace EOS.VectorStore.Tests;

public class ChromaVectorStoreTests
{
    private static string ChromaDbEndpoint =>
        Environment.GetEnvironmentVariable("EOS_CHROMADB_ENDPOINT")
        ?? throw new InvalidOperationException("EOS_CHROMADB_ENDPOINT is not set.");

    [Fact]
    public async Task IndexAsync_WritesTheEmbeddingToTheRealChromaDbInstance()
    {
        var store = new ChromaVectorStore(ChromaDbEndpoint);
        var id = Guid.NewGuid();
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };

        await store.IndexAsync(id, embedding, CancellationToken.None);

        using var client = new HttpClient { BaseAddress = new Uri(ChromaDbEndpoint) };
        const string collectionsPath = "api/v2/tenants/default_tenant/databases/default_database/collections";
        var listResponse = await client.GetAsync($"{collectionsPath}");
        listResponse.EnsureSuccessStatusCode();
        var collections = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var collectionId = collections.EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "eos-knowledge-embeddings")
            .GetProperty("id").GetString();

        var getResponse = await client.PostAsJsonAsync(
            $"{collectionsPath}/{collectionId}/get",
            new { ids = new[] { id.ToString() }, include = new[] { "embeddings" } });
        getResponse.EnsureSuccessStatusCode();
        var body = await getResponse.Content.ReadFromJsonAsync<JsonElement>();

        var returnedIds = body.GetProperty("ids").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(id.ToString(), returnedIds);
    }
}
