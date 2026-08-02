using EOS.Contracts;

namespace EOS.Reasoning;

/// <summary>
/// Composition Root Adapter (WP-019 Implementation Plan Revision 3, Area 1) for
/// Reasoning-Engine-Specification-v1.0 §12.1's <c>assemble_context()</c> call. §12 requires the
/// Reasoning Engine to never query <c>EOS.KnowledgeGraph</c>/<c>EOS.VectorStore</c> directly and
/// to acquire context only "via <c>IKnowledgeClient</c>," but Constitution Part 1 §1.2 does not
/// list <c>EOS.Knowledge</c> among <c>EOS.Reasoning</c>'s allowed dependencies. This small
/// interface is defined here, in <c>EOS.Reasoning</c>; the concrete adapter is supplied by
/// <c>EOS.Runner</c>'s composition root (<c>Program.cs</c>), which does hold a legal
/// <c>EOS.Knowledge</c> reference — identical in shape to <c>ISummarizer</c> (WP-016) and
/// <c>ICompareProvider</c> (WP-018).
/// </summary>
public interface IContextAcquisitionProvider
{
    Task<AcquiredContext> AcquireContextAsync(
        ReasoningContextScope scope, CancellationToken cancellationToken = default);
}
