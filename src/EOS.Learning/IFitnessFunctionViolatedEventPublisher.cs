namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>FitnessFunctionViolated</c> event (new in v1.1) —
/// payload frozen exactly as specified: "fitness_function_id, observed_value, threshold".
/// </summary>
public interface IFitnessFunctionViolatedEventPublisher
{
    void PublishFitnessFunctionViolated(string fitnessFunctionId, double observedValue, double threshold);
}
