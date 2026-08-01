namespace EOS.KnowledgeGraph;

/// <summary>
/// Knowledge-Management-Specification-v1.0 §12.6/§16.5/§21.2's append-only Version chain entry
/// (FR-KM6) — never a separate physical store (FR-KM1); held as an element of
/// <see cref="KnowledgeMetadata.VersionHistory"/>. Each entry's predecessor is the prior element
/// of that same ordered, append-only list — no explicit predecessor reference field is needed
/// (mirrors <see cref="RelationshipEdge"/>'s minimal-fields precedent). Distinct from
/// Constitution Part 8's Artifact Registry file-versioning (§16.5), which continues to version
/// the underlying evidence artifacts a knowledge object's <see cref="KnowledgeMetadata.Source"/>
/// references.
/// </summary>
public sealed record VersionRecord
{
    public required int Version { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Reason { get; init; }
}
