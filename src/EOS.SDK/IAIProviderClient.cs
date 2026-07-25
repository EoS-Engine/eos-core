namespace EOS.SDK;

public interface IAIProviderClient
{
    Task<InferenceResult> InferAsync(InferenceRequest request, CancellationToken cancellationToken = default);
}
