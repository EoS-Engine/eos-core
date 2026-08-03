namespace EOS.Resources;

/// <summary>
/// Resource-Management-Specification-v1.0 §17.1–§17.4/§17.5's per-resource-type Warning/
/// Critical/Emergency boundaries. Plain, <c>EOS.SharedKernel</c>-free record — mirrors
/// <c>ReasoningEngineOptions</c>'s precedent (WP-019): <c>EOS.Resources</c>'s dependency row
/// (Constitution Part 1 §1.2: "EOS.Contracts, EOS.SDK") does not include <c>EOS.SharedKernel</c>,
/// so <c>Program.cs</c> maps <c>ThresholdsOptions</c>' fields into this type at the composition
/// root rather than this project referencing <c>ThresholdsOptions</c> directly.
/// </summary>
public sealed record ResourceTierBoundaries(double Warning, double Critical, double Emergency);
