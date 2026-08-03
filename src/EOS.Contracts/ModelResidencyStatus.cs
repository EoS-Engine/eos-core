namespace EOS.Contracts;

/// <summary>
/// Resource-Management-Specification-v1.0 §21.1's <c>get_model_residency</c> read-only signal:
/// "reports whether a given model is currently resident and what loading it would cost" (§14.3).
/// <see cref="RamFootprintMegabytes"/> is <c>null</c> until this model's footprint has been
/// empirically observed at least once (WP-022 Implementation Plan Decision D3) — never a
/// fabricated estimate, per FR-RM2.
/// </summary>
public sealed record ModelResidencyStatus(string ModelId, ModelResidencyState State, double? RamFootprintMegabytes);
