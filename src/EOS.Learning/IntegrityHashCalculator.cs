using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EOS.Contracts;

namespace EOS.Learning;

/// <summary>
/// WP-027 Decision 3 (locked): SHA-256 over the canonical UTF-8 representation of
/// <c>RecordId, FromStage, ToStage, TriggeredBy, EvidenceRefs, OccurredAt</c>, in that exact
/// order, newline-joined. No previous-record chaining — each hash is self-contained. Uses the
/// existing .NET BCL cryptography APIs only, no new package.
///
/// Determinism: <see cref="TransitionRecord.EvidenceRefs"/> is serialized as a canonical JSON
/// array via the ordering the caller already supplied — this calculator never reorders it (the
/// caller is responsible for supplying a stable order; §9 does not define an intrinsic ordering
/// rule for evidence references beyond "array", so array-index order, as given, is the only
/// non-invented interpretation). <see cref="TransitionRecord.OccurredAt"/> is normalized to UTC
/// and formatted with the round-trip ("O") specifier — the same input instant always produces
/// the same string regardless of the caller's original offset.
/// </summary>
public static class IntegrityHashCalculator
{
    public static string Compute(
        Guid recordId, PipelineStage fromStage, PipelineStage toStage, string triggeredBy,
        string[] evidenceRefs, DateTimeOffset occurredAt)
    {
        var canonical = string.Join(
            '\n',
            recordId.ToString(),
            fromStage.ToString(),
            toStage.ToString(),
            triggeredBy,
            JsonSerializer.Serialize(evidenceRefs),
            occurredAt.ToUniversalTime().ToString("O"));

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexStringLower(hashBytes);
    }

    public static string Compute(TransitionRecord record) =>
        Compute(record.RecordId, record.FromStage, record.ToStage, record.TriggeredBy, record.EvidenceRefs, record.OccurredAt);
}
