namespace EOS.Knowledge;

/// <summary>
/// Composition Root Adapter (ADR-015-001) for §19's <c>KnowledgeDriftDetected</c> event
/// ("Freshness Manager (§17.6)", payload "node_id, drift_description"). Interface defined for
/// event ownership completeness (Traceability Matrix: "All Knowledge Management events... WP-018
/// (remainder)"); intentionally unwired to any automatic trigger this WP. §17.6's Content Drift
/// Detection requires comparing a knowledge object's referenced facts against "current live
/// configuration/state values it cites" — no approved interface in this WP's scope resolves what
/// a knowledge object "cites" against live system state, and inventing one would be speculative
/// engineering beyond WP-018's roadmap-assigned scope (Included Components: "Freshness scoring
/// and drift detection" as a category, with no concrete mechanism specified).
/// </summary>
public interface IKnowledgeDriftDetectedEventPublisher
{
    void PublishKnowledgeDriftDetected(Guid nodeId, string driftDescription);
}
