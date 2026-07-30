namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §14/§16.2's embedding-generation reachability, per
/// ADR-015-001's Composition Root Adapter Pattern: <c>EOS.Knowledge</c> defines this small,
/// BCL-typed interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter
/// wrapping <c>AIProviderManager</c> (<c>EOS.AIProvider</c>), which <c>EOS.Knowledge</c> has no
/// legal dependency path to reach directly.
/// </summary>
public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<float>> EmbedAsync(string content, CancellationToken cancellationToken = default);
}
