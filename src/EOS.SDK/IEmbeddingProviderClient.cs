namespace EOS.SDK;

public interface IEmbeddingProviderClient
{
    Task<Vector> EmbedAsync(string content, CancellationToken cancellationToken = default);
}
