namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §16.2/§20.1's <c>MemoryRef</c>, per ADR-015-004:
/// combines the already-ratified <see cref="Knowledge.MemoryType"/> (WP-014) with a string
/// key locating the source within that memory type's backing store.
/// </summary>
public sealed record MemoryRef(MemoryType Type, string Key);
