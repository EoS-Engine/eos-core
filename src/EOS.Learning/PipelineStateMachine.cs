using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// Learning-Engine-Specification-v1.1 §16's <em>stage</em> transition edges — the exact edges
/// listed there, no invented transitions. Shared by <see cref="StageEngine"/> (guards against
/// executing an invalid transition) and <see cref="IntegrityChecker"/> (§11.6's
/// <c>StateMachine.is_valid_edge(from_stage, to_stage)</c>).
///
/// Quarantine is deliberately not represented here: it is a <see cref="PipelineRecordStatus"/>
/// change, not a <see cref="PipelineStage"/> transition — WP-026's own precedent (<c>Ingestion</c>'s
/// quarantine path) leaves <c>Stage</c> untouched and only changes <c>Status</c>, and
/// <c>PipelineStage</c> itself has no "Quarantined" value. <see cref="TransitionRecord"/> (§9)
/// is defined purely in terms of <c>from_stage</c>/<c>to_stage</c>, so Quarantine events are
/// tracked via <c>LessonQuarantined</c>/<c>DataIntegrityViolationDetected</c>, never as a
/// <see cref="TransitionRecord"/> entry. Demotion edges (§16's "Any stage --(contradicting
/// evidence)--&gt; Demoted...") are also deliberately not represented — WP-027 does not
/// implement Demotion (disclosed, out of scope; not named in the roadmap's WP-027 "Included
/// components").
/// </summary>
public static class PipelineStateMachine
{
    private static readonly HashSet<(PipelineStage From, PipelineStage To)> ForwardEdges =
    [
        (PipelineStage.Lesson, PipelineStage.Pattern),
        (PipelineStage.Pattern, PipelineStage.BestPractice),
        (PipelineStage.BestPractice, PipelineStage.Principle),
        (PipelineStage.Principle, PipelineStage.GoldenPath),
        (PipelineStage.GoldenPath, PipelineStage.Automation),
        (PipelineStage.Automation, PipelineStage.ReusableComponent),
        (PipelineStage.ReusableComponent, PipelineStage.PlatformCapability),
    ];

    public static bool IsValidEdge(PipelineStage from, PipelineStage to) => ForwardEdges.Contains((from, to));
}
