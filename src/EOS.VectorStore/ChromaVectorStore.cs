using System.Net.Http.Json;
using System.Text.Json;

namespace EOS.VectorStore;

/// <summary>
/// Memory-Management-Specification-v1.0 §16.2's <c>VectorStore.index(episodic_entry.id,
/// embedding)</c> — the first production write against the real ChromaDB instance
/// (Constitution Part 4 §4.1: "ChromaDB (via EOS.VectorStore) | Embeddings for Knowledge
/// Graph semantic search"). <paramref name="id"/> corresponds to <c>EpisodicEntryRef</c>
/// (<c>Guid</c>, per <c>WP-015-Specification-Clarifications.md</c> Item 1); the embedding is
/// represented as the same <c>IReadOnlyList&lt;float&gt;</c> shape <c>EOS.SDK.Vector</c> itself
/// wraps, without <c>EOS.VectorStore</c> taking a dependency on <c>EOS.SDK</c> (no such edge is
/// granted by Constitution Part 1 §1.2).
/// </summary>
public sealed class ChromaVectorStore(string chromaDbEndpoint)
{
    private const string CollectionsPath = "api/v2/tenants/default_tenant/databases/default_database/collections";
    private const string CollectionName = "eos-knowledge-embeddings";

    public async Task IndexAsync(Guid id, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient { BaseAddress = new Uri(chromaDbEndpoint) };

        var collectionId = await EnsureCollectionAsync(client, cancellationToken);

        var addResponse = await client.PostAsJsonAsync(
            $"{CollectionsPath}/{collectionId}/add",
            new { ids = new[] { id.ToString() }, embeddings = new[] { embedding } },
            cancellationToken);
        addResponse.EnsureSuccessStatusCode();
    }

    private static async Task<string> EnsureCollectionAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            CollectionsPath,
            new { name = CollectionName, get_or_create = true },
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return body.GetProperty("id").GetString()!;
    }
}
