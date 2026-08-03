using EOS.Contracts;

namespace EOS.Knowledge;

/// <summary>
/// Resource-Management-Specification-v1.0 §21.1's <c>request_background_slot</c> (WP-022),
/// per the Composition Root Adapter Pattern (ADR-015-001) — <c>EOS.Knowledge</c> defines this
/// small interface; <c>EOS.Runner</c>'s <c>Program.cs</c> supplies the concrete adapter bridging
/// to <c>IResourceManagementClient</c>/<c>EventMediator</c> (<c>EOS.Contracts</c>/
/// <c>EOS.Orchestrator</c>), which <c>EOS.Knowledge</c> has no legal dependency path to reach
/// directly. <see cref="IResourceManagementClient.RequestBackgroundSlot"/> is <c>void</c>
/// (§21.1) — this interface's <see cref="bool"/> return is the composition root's own
/// synchronous correlation of the resulting <c>BackgroundJobGranted</c>/
/// <c>BackgroundJobDeferred</c> event (§20), never a redesign of the published interface.
/// </summary>
public interface IBackgroundSlotRequester
{
    bool RequestSlot(string jobId, ResourceClass resourceClass);
}
