namespace EOS.Learning;

/// <summary>
/// Persistence for <see cref="EOS.Contracts.IngestionRateGuardState"/> — must survive process
/// restarts (§11.1/§24.1's Knowledge Poisoning defense is only meaningful if a restart cannot
/// reset an attacker's counter).
/// </summary>
public interface IIngestionRateGuardStore
{
    Task EnsureTableExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically increments (creating the bucket row if absent) the event count for
    /// <paramref name="producerRole"/>'s wall-clock-anchored window
    /// [<paramref name="windowStart"/>, <paramref name="windowEnd"/>) and returns the new count,
    /// including this event.
    /// </summary>
    Task<int> IncrementAndGetCountAsync(
        string producerRole, DateTimeOffset windowStart, DateTimeOffset windowEnd, CancellationToken cancellationToken = default);
}
