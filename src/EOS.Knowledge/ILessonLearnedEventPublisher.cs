namespace EOS.Knowledge;

/// <summary>
/// Memory-Management-Specification-v1.0 §16.2's <c>LessonLearned</c> emission, per
/// ADR-015-002's trigger-dependent producer rule (real emission for the explicit-role,
/// <c>IncidentResolved</c>, and session-close triggers; suppressed for the Gate-failure
/// trigger, since <c>EOS.Gates</c> already emits it per Constitution §0.8.3). Per the
/// Composition Root Adapter Pattern (ADR-015-001): <c>EOS.Knowledge</c> defines this small,
/// BCL-typed interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter
/// bridging to <c>EventEnvelope</c>/<c>EventMediator</c>, which <c>EOS.Knowledge</c> has no
/// legal dependency path to reach directly.
/// </summary>
public interface ILessonLearnedEventPublisher
{
    void PublishLessonLearned(Guid episodicEntryId, string source);
}
