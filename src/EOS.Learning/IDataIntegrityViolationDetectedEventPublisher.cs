using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §15's <c>DataIntegrityViolationDetected</c> event (new in
/// v1.1) — payload frozen exactly as specified: "record_id, from_stage, to_stage".
/// </summary>
public interface IDataIntegrityViolationDetectedEventPublisher
{
    void PublishDataIntegrityViolationDetected(Guid recordId, PipelineStage fromStage, PipelineStage toStage);
}
