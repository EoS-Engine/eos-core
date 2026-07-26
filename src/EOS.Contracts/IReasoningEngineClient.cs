namespace EOS.Contracts;

public interface IReasoningEngineClient
{
    Task<Decision[]> ReasonAsync(ReasoningRequest request, CancellationToken cancellationToken = default);
}
