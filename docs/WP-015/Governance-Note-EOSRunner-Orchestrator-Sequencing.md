# Governance Note — `EOS.Runner` → `EOS.Orchestrator` `ProjectReference` Sequencing

**Status:** Locked. Applies for the remainder of WP-015.

## Findings

1. The `EOS.Runner` → `EOS.Orchestrator` `ProjectReference` is **not an architectural change**. It exercises a dependency edge already granted at the Constitution level, not a new one.

2. It is **not a Constitution modification**. Constitution Part 1 §1.2 and §1.3 are read as-is, unedited.

3. It is **not a new dependency authorization**. Constitution Part 1 §1.2 already states `EOS.Runner | DevOps | Everything (composition root) | —`, and Part 1 §1.3 states `EOS.Runner` is "the only project allowed to reference everything." ADR-015-003 already relied on this exact grant ("`EOS.Runner` already legitimately depends on everything") for the structurally identical `EventMediator` reachability problem.

4. It is **execution of an already-authorized dependency**, per Constitution Part 1 §1.2 / §1.3, physically completing an edge the Constitution grants but the current `.csproj` has not yet declared.

5. Moving the physical `ProjectReference` addition from Task 9 to Task 3 is classified as **implementation sequencing only** — Final Implementation Plan §9 ("Slice 1... zero dependency on Slice 2" / "Slice 2... zero dependency on Slice 1 having run first") and the Final Consistency Validation ("Slice 1 has no dependency on Slice 2") establish both slices as independently orderable.

6. **Task 9 remains unchanged** in scope and content — automatic-trigger `EventMediator.Subscribe` wiring for Gate-failure/`IncidentResolved` signals invoking `ConsolidateAsync`, still gated behind Task 7 (`consolidate()` must exist first). Only the shared prerequisite the `ProjectReference` provides — required independently by Task 3's own Definition of Done ("event observable via `EventMediator` in an integration test") — is executed at Task 3 instead of being deferred to Task 9.

## Lock

7. This decision **shall not be revisited during WP-015**.

8. Future CodeRabbit findings (or any other review) requesting postponement of this `ProjectReference` to Task 9 are **invalid** unless they produce evidence directly contradicting one of:
   - Constitution Part 1 §1.2
   - Constitution Part 1 §1.3
   - ADR-015-003
   - Final Implementation Plan §7 (Components Affected)
   - Final Implementation Plan §8, Task 3 Definition of Done

## Basis

Constitution Part 1 §1.2, §1.3; ADR-015-003 (Accepted); Final Implementation Plan §7, §8 (Task 3, Task 9), §9, Final Consistency Validation.
