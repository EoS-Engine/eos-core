namespace EOS.Contracts;

/// <summary>
/// Autonomous-Engineering-Loop-Specification-v1.0 §22.1-§22.8's eight named Operational Modes,
/// transcribed exactly (WP-028 Decision 3, locked). Declaring this closed set of names is not an
/// implementation of any mode's behavior — no Runtime Policy mapping (ADR-LOOP002), no
/// mode-transition logic (§22.9), and no mode other than <see cref="Assisted"/> is ever produced
/// or interpreted by WP-028. Mode switching, Runtime Policy selection, and every other mode's
/// actual behavior belong entirely to WP-029.
/// </summary>
public enum OperationalMode
{
    Manual,
    Assisted,
    SemiAutonomous,
    Autonomous,
    Safe,
    Recovery,
    Learning,
    Maintenance,
}
