# Governance Change Proposal 001

Status: Proposed — not applied. No approved document has been modified by this proposal.
Origin: Final Independent Architecture Governance Board, adversarial validation pass over findings raised across the WP-016–WP-030 pre-implementation governance audit series.
Scope: This proposal lists only the two findings that reached **Proven** confidence after repeated, independent, evidence-exhausted falsification attempts. Findings below Proven are excluded per Board rule.

---

## Item 1 — Traceability Matrix: False Artifact Registry Ownership Attribution

**Exact document:** `docs/EOS-Implementation-Roadmap-v1.0.md`

**Exact section:** Architecture Traceability Matrix, row: `| Artifact Registry (Part 8) | Services | WP-004 (foundation), used throughout |`

**Exact reason:** The row asserts WP-004 implements Artifact Registry "as foundation." WP-004's own roadmap row scopes itself exclusively to "connection management and health-check probes for all four stores... no domain schema yet beyond what proves connectivity" — no hash-addressing, immutable versioning, or typed artifact indexing (Constitution Part 8 §8.2/§8.3) is included in that scope. `docs/work-packages/WP-004-Completion-Report.md` contains zero references to "artifact" anywhere in its text, confirming the built result matches the narrow scope, not the Traceability Matrix's broader claim. No other WP in the 30-WP roadmap claims ownership of Artifact Registry's defining capabilities.

**Exact evidence:**
- `docs/EOS-Implementation-Roadmap-v1.0.md`, WP-004 row, "Included components" field (self-scoping quote above).
- `docs/work-packages/WP-004-Completion-Report.md` — full-text search for "artifact" returns zero matches.
- `docs/EOS-Specification.md` Part 8 §8.1–§8.3 (Artifact Registry's defining requirements: versioned, content-addressed/hash, indexed for query, immutable-with-new-version-on-change).
- Repository-wide search of `src/` for `ArtifactRegistry`/hash-addressed-artifact implementation code returns zero matches.

**Exact constitutional justification:** Constitution §0.1.1.1 (Evidence over assertion) and Part 6's Task Lifecycle requirement that "a `TaskCompleted` event must reference passing evidence in the Artifact Registry (Part 8), not just a status flag" both depend on Artifact Registry being real. A false ownership attribution in the Traceability Matrix — the document whose own stated purpose is to verify "every architecture component is implemented exactly once" (Dependency Validation section) — directly undermines the roadmap's own internal audit mechanism for the reason the Constitution requires evidence-based verification in the first place.

---

## Item 2 — Traceability Matrix / Gap Summary: Undisclosed Runtime-Host Gap

**Exact document:** `docs/EOS-Implementation-Roadmap-v1.0.md`

**Exact section:** Architecture Traceability Matrix, "Summary of Reported Gaps" (currently lists exactly three items: Prompt Registry tooling, File System/Git Event trigger detection, `EOS.Pipeline`).

**Exact reason:** No WP among WP-016 through WP-030 (the full set audited across this governance series) allocates ownership of a daemon, hosted service, timer, or scheduler process capable of driving Sprint-cycle/Daily-cycle-anchored batch sweeps (Constitution §0.12.1) or the Autonomous Engineering Loop's "Repeat" step (`Autonomous-Engineering-Loop-Specification-v1.0.md` §7.1, step 18) without a human manually re-invoking `EOS.Runner`. This is a real, roadmap-wide gap of the same category and severity as the File System/Git Event trigger gap already disclosed in this exact section — but unlike that gap, it is currently undisclosed anywhere in the roadmap's own gap-reporting mechanism.

**Exact evidence:**
- `src/EOS.Runner/Program.cs` — builds a host, calls `RunAsync()` once, exits (direct code observation).
- `src/EOS.Orchestrator/` — contains only `EventMediator.cs`, no scheduling component (direct code observation).
- `docs/Infrastructure-and-Implementation-Roadmap-v1.0.md` line 166 (`EOS.Runner` "starts, executes... and logs a 'Ready' state") and line 376 (only cron job anywhere is the Phase 8 daily backup).
- `docs/WP-010-Implementation-Plan.md` line 20 and `docs/WP-012-Implementation-Plan.md` line 29 — implementer-authored, contemporaneous admissions that "no real Scheduler budget infrastructure exists anywhere in this codebase yet."
- `docs/EOS-Implementation-Roadmap-v1.0.md`, WP-028 row, "Why this exists" field ("turns eight independently-capable subsystems into one continuously-operating autonomous system") versus its own "Expected deliverables" field (a single manually-triggered iteration) — an internal scope/framing gap the roadmap does not reconcile or disclose.

**Exact constitutional justification:** Constitution §0.12.1 defines Daily-cycle (24h) and Sprint-cycle (1–2 week) cadences as load-bearing, cited by name across multiple approved specifications (Learning Engine's Stall/Fitness/Integrity sweeps, Resource Management's sampling cadence, Planning & Execution Engine's Periodic Execution mode) as their own scheduling mechanism. The roadmap's own Dependency Validation section states its purpose is to verify "no missing implementation exists, with two explicitly acknowledged and justified exceptions" (naming File/Git triggers and `EOS.Pipeline` only) — the runtime-host gap is a missing implementation of the same disclosure-worthy character that this section's own stated completeness standard requires it to name, consistent with the precedent the roadmap itself already set for the File/Git Event trigger gap.

---

## What this proposal does not do

This document does not modify `EOS-Implementation-Roadmap-v1.0.md`, the Constitution, or any specification. It does not move, split, or create any Work Package. It does not propose an implementation approach for either gap. It is submitted for whoever holds authority over the frozen architecture to accept, modify, or reject.
