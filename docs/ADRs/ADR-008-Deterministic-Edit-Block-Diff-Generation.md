# ADR-008 — Deterministic Edit-Block Diff Generation

## 1. Status

**Accepted** — approved as the Architecture / Capability Review "Real-Model Diff Generation Reliability" and authorized for implementation as a correction to WP-A (working name; not a roadmap Work Package number).

- **adr_id:** `ADR-008`
- **approver:** Product Owner / Architecture Board, per Decision Matrix §0.6
- **conditions:** changes confined to `EOS.SeniorEngineer` (the role's prompt contract, edit-block parsing/validation/in-memory application, and a deterministic `UnifiedDiffBuilder`) and its tests. No contract, infrastructure, orchestration, configuration, or specification change. ADR-007 unchanged.

## 2. Problem

With the model asked to author a unified diff directly, the real `qwen2.5-coder:7b` (CPU) output was rejected by ADR-007's `git apply --check` in every real run (`corrupt patch at line 18` / `line 15`), and the one artifact registered before ADR-007 did not implement the requested change.

## 3. Observed Evidence

Line-by-line analysis of that artifact showed every context line was a verbatim line of the supplied file; what was wrong was positional bookkeeping — hunk start `19` (real: 32), new-side count `10` for 12 lines, existing lines re-emitted as `+`, fabricated `index` lines. Two direct probes with an exact edit-block format produced the correct `/health` route and test; with short anchors both SEARCH blocks matched the real files strictly and uniquely (302 output tokens). The model copies existing lines verbatim; it cannot count lines or maintain `+`/` ` markers across a hunk.

## 4. Decision

The model **never produces a diff**. It produces exact edit blocks:

```
[EDIT]
FILE: <referenced path>
SEARCH:
<1–6 lines copied verbatim from the file>
REPLACE:
<those lines with the change applied>
[/EDIT]
```

`SeniorEngineer` (one `ReasonAsync` call, unchanged) then, in order: `ExtractEditBlocks → ApplyEditBlocks (in-memory copies only) → UnifiedDiffBuilder → ValidateUnifiedDiff (unchanged) → CheckPatchAppliesAsync (ADR-007, unchanged) → RegisterAsync (unchanged)`.

Deterministic rules, enforced by the role and never relaxed: the path must be one of the human-referenced files (root-relative, under `src/`/`tests/`, no traversal); for an existing file SEARCH must be non-empty and occur exactly once, character-for-character, whitespace-significant, aligned to whole lines — no fuzzy matching, normalization or re-indentation; an empty SEARCH is permitted only for a file the workspace reader reported as missing (creation; REPLACE is the complete content, given a trailing newline); blocks apply in supplied order against the current in-memory content, and a later block whose SEARCH has become absent or ambiguous fails closed; edits that change nothing fail. `UnifiedDiffBuilder` is a pure function of (original, modified): LCS line diff, 3 context lines, git hunk layout, `/dev/null` for creation/deletion, `\ No newline at end of file` semantics — hunk offsets, counts, markers and paths are never under model control.

Any failure (malformed block, unknown/out-of-scope path, SEARCH not found / not unique / whitespace mismatch, invalid creation, builder failure, structural failure, `git apply --check` failure) throws before registration and follows the existing `Running → Blocked → TaskBlocked` path: no artifact, no `TaskCompleted`, no workspace mutation, no retry, no repair.

## 5. Alternatives Rejected

Keep asking for diffs with more instruction (cannot fix arithmetic); `--recount`/fuzzy or indentation-tolerant matching (hides model error; strict matching is demonstrably achievable); full-file replacement (2–3 k output tokens, several minutes per file on CPU; silent drops elsewhere in the file); two-stage generation (doubles latency, adds nothing); build/test in the autonomous path (Review/Testing responsibility, unchanged).

## 6. Consequences

`tests/EOS.SeniorEngineer.Tests/{EditBlockTests,UnifiedDiffBuilderTests,SeniorEngineerTests}.cs` prove parsing, strict validation, deterministic application, the builder's output (every generated diff round-tripped through a real `git apply --check` in an isolated temporary repository), and the unchanged failure semantics; `FirstExecutionSliceAcceptanceTests` proves edit blocks → derived diff → real applicability check → artifact → `Review`/`TaskCompleted`, and that a stale read fails closed. `SeniorEngineer.ExtractDiffFence` remains only because its existing tests lie outside this change set. The guarantee remains applicability, not intent, compilation, or tests.
