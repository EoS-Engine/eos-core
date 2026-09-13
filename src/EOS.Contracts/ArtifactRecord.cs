namespace EOS.Contracts;

/// <summary>
/// One immutable, content-addressed entry in Constitution Part 8's Artifact Registry
/// (Post-Roadmap WP-A). <see cref="ContentHash"/> — the lowercase 64-character hexadecimal
/// SHA-256 of <see cref="Content"/>'s exact UTF-8 bytes, with no normalization — is the
/// artifact's sole identity (§8.1 "content-addressed (hash)"). <see cref="Type"/>,
/// <see cref="Producer"/>, <see cref="PreviousVersionHash"/>, and <see cref="RegisteredAt"/>
/// are metadata: never hashed, never part of the identity. <see cref="PreviousVersionHash"/>
/// realizes §8.3's "a 'change' creates a new version referencing the prior version's hash".
/// </summary>
public sealed record ArtifactRecord(
    string ContentHash,
    string Type,
    string Producer,
    string Content,
    string? PreviousVersionHash,
    DateTimeOffset RegisteredAt);
