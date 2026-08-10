namespace EOS.Contracts;

/// <summary>
/// Learning-Engine-Specification-v1.1 §12's <c>IngestionRateGuardState</c> — a persisted,
/// wall-clock-bucket-anchored per-producer-role window used by §11.1's
/// <c>IngestionRateGuard.exceeds_threshold(producer_role, window)</c>. Persisted (not
/// in-memory) so the guard remains correct across process restarts.
/// </summary>
public sealed record IngestionRateGuardState(
    string ProducerRole,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    int EventCount);
