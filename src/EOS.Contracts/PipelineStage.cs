namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §9's <c>PipelineRecord.stage</c> vocabulary — "exactly the
/// Constitution Part 14 stage names... spelled identically, so there is exactly one vocabulary
/// for pipeline stages across the entire EOS, not two."
/// </summary>
public enum PipelineStage
{
    Lesson,
    Pattern,
    BestPractice,
    Principle,
    GoldenPath,
    Automation,
    ReusableComponent,
    PlatformCapability,
}
