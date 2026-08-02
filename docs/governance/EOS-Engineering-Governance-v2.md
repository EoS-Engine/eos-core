# EOS Engineering Governance v2 — Architecture Baseline Freeze & Review Policy

**Document Type:** Project Governance (process only)
**Modifies:** Nothing. This document does not change `docs/EOS-Specification.md`, `docs/EOS-Implementation-Roadmap-v1.0.md`, `docs/Development-Workflow.md`, any subsystem specification, any ADR, or any implementation. It defines only the review workflow for WP-019 through WP-030.

## 1. Scope

This governance applies starting immediately after WP-018 completion.

Current repository state at the time this document is created:

- WP-018 is complete.
- The repository is still on the WP-018 feature branch (`wp-018-knowledge-management-quality-governance-freshness-reuse`).
- PR #15 has not been merged.

Repository closure actions for WP-018 (merging PR #15, tagging the release, archiving the completion report, deleting the feature branch) are outside the scope of this document and will happen separately.

Once repository closure occurs, this governance continues to apply, without modification, through WP-030.

## 2. Frozen Architecture Baseline

The architecture implemented by WP-001 through WP-018 is considered the official project baseline under the currently available evidence.

This does not mean the baseline is infallible. It means that, as of this document, no evidence-supported blocker against WP-019 through WP-030 currently exists.

## 3. Baseline Freeze Rule

The baseline SHALL remain frozen.

It SHALL NOT be reopened merely because:

- a reviewer wants another architecture audit,
- another hostile review is requested,
- assumptions are questioned again,
- previous discussions are repeated,
- already-reviewed documents are re-analyzed.

## 4. Reopening Criteria

The architecture may be reopened only if ALL of the following are true:

1. New evidence exists.
2. That evidence was unavailable during the WP-001–WP-018 audit process.
3. The evidence proves one of:
   - Constitution violation
   - Specification violation
   - Roadmap violation
   - Development Workflow violation
   - Public API regression
   - Cross-WP regression
   - Build regression
   - Test regression
   - Architecture blocker
4. The issue cannot be solved additively.
5. Solving it requires changing a frozen architecture artifact.

If any one condition is false, the architecture remains frozen.

## 5. Delta Review Policy

Starting with WP-019, every review SHALL review only:

- the active Work Package, and
- regressions introduced by that Work Package.

No previously closed Work Package is reviewed again unless the Reopening Criteria (§4) are satisfied.

## 6. Engineering Debt Policy

Engineering Debt remains frozen until its recorded trigger.

Previously recorded trigger points remain authoritative. Debt items shall not be re-discussed before their recorded trigger is reached by an active Work Package.

## 7. Architecture Gap Policy

Architecture Gaps remain governance items, reviewed only by governance.

They do not block implementation unless a Gap becomes part of a Work Package's own binding acceptance criteria (Test Verification / Demo-Acceptance criteria in that WP's roadmap row).

## 8. Review Termination Policy

A Work Package review terminates when:

- Build passes
- Tests pass
- Formatting passes
- No blocking findings remain

After that point, Architecture Review ends and Hostile Review ends for that Work Package. Only normal Code Review and Delta Review continue.

## 9. Future Review Workflow

Every future Work Package review SHALL begin with:

**STEP 0** — Read this governance document. Validate whether the Reopening Criteria (§4) are satisfied for any claim that would otherwise trigger an architecture review.

If the Reopening Criteria are not satisfied: do not perform an Architecture Review. Proceed directly to Delta Review (§5) of the active Work Package.

## 10. Authority

This document is part of the project's engineering governance.

Future reviewers, human or AI, follow this workflow. The architecture baseline established by WP-001 through WP-018 is considered frozen unless the Reopening Criteria in §4 are satisfied.
