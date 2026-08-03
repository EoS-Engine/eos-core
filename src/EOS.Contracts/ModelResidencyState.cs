namespace EOS.Contracts;

/// <summary>
/// Resource-Management-Specification-v1.0 §22's Per-model Residency State model:
/// <c>Unloaded → Loading → Resident → (Idle-Timeout | Memory-Pressure) → Unloading → Unloaded</c>.
/// Idle-Timeout/Memory-Pressure are transition reasons, not additional states.
/// </summary>
public enum ModelResidencyState
{
    Unloaded,

    /// <summary>
    /// Named by §22's State Model, but currently unreachable: no frozen document (this
    /// specification or AI-Provider-Layer-Specification-v1.0) defines a signal marking the start
    /// of a model load distinct from its completion — <c>InferenceRouted</c> fires only after the
    /// model is already resident (WP-022 Recovery Plan Slice R3/Finding F5). Retained rather than
    /// removed, since §22 names it as part of the frozen state model; would become reachable only
    /// if a future WP adds such a signal.
    /// </summary>
    Loading,
    Resident,
    Unloading,
}
