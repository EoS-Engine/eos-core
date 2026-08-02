namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §9's <c>PipelineRecord.status</c> vocabulary.
/// </summary>
public enum PipelineRecordStatus
{
    Active,
    Stalled,
    Archived,
    Demoted,
    Quarantined,
}
