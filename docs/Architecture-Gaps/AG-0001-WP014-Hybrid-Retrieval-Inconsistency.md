# WP-014 Hybrid Retrieval Specification Inconsistency

## Status

Architecture Gap

Not an implementation bug.

## Discovery

This inconsistency was discovered during WP-014 implementation, and only after implementation reached the retrieval layer itself — specifically while implementing `IKnowledgeClient.query()` and `IKnowledgeClient.query_similar()` against their exact, ratified signatures. It was not visible during WP-014's Architecture Review, Architecture Challenge, or Implementation Plan phases, all of which worked from the roadmap's and specification's summary-level descriptions rather than from the literal method signatures. The gap surfaced only when an attempt was made to identify a concrete source of the "query embedding" that a vector-similarity stage would require, and no such source could be found in either method's actual parameter list.

## Source Documents

- `docs/EOS-Specification.md` (the Constitution)
- `docs/Memory-Management-Specification-v1.0.md`
- `docs/Learning-Engine-Specification-v1.1.md`
- `docs/EOS-Implementation-Roadmap-v1.0.md`

## Evidence

| Document | Section | Exact Quote | Interpretation |
|---|---|---|---|
| EOS-Specification.md | §0.5.2 | "All roles query the Knowledge Graph through `EOS.Knowledge` (never directly through `EOS.VectorStore` — see Part 2 dependency rules). Queries are hybrid: symbolic graph traversal + vector similarity re-ranking." | States, at the Constitution level, that querying the Knowledge Graph is hybrid overall — does not specify which method(s) of `IKnowledgeClient` perform which stage. |
| Memory-Management-Specification-v1.0.md | §13 | "1. **Symbolic stage:** `EOS.KnowledgeGraph` resolves an initial candidate set via structured filters (memory type, `domain_tags`, node type per §0.5.1, time range). 2. **Vector stage:** `EOS.VectorStore` (ChromaDB, infrastructure only) computes embedding-distance scores for the candidate set against a query embedding. 3. **Re-ranking stage:** `MemoryRouter`/`ContextAssembler` combine symbolic recency/relevance signals with vector distance into a single ranked list (§19)" | Names `ContextAssembler` as a participant in the vector/re-ranking stages. `ContextAssembler` is, per §9, the internal component belonging to `assemble_context()` (§15). |
| Memory-Management-Specification-v1.0.md | §9 (component diagram) | "Router --> Graph", "Assembler --> Graph", "Assembler --> Vec", "Lifecycle --> Graph", "Lifecycle --> Vec" | `MemoryRouter` (the component most associated with `query()`/`query_similar()`'s memory-type-driven dispatch) has an edge to `EOS.KnowledgeGraph` only, never to `EOS.VectorStore`. Only `ContextAssembler` and `LifecycleEngine` have edges to `EOS.VectorStore`. |
| Memory-Management-Specification-v1.0.md | §23.2 (sequence diagram) | "Role->>Memory: assemble_context(request incl. budget, scope)" / "Memory->>KG: query(type filters, domain_tags)" / "KG-->>Memory: symbolic candidates" / "Memory->>VS: similarity(query_embedding, candidates)" | Shows the vector-similarity call occurring inside the `assemble_context()` flow, as a step subsequent to and separate from `query()`, which itself returns only "symbolic candidates." |
| Memory-Management-Specification-v1.0.md | §20.1 | "IEnumerable<KnowledgeNode> query_similar(KnowledgeGraphRef ref)" / "IEnumerable<KnowledgeNode> query(MemoryType type?, string[] domain_tags?, DateRange range?)  // NEW, §10" | The literal, ratified signatures of both methods. Neither contains a free-text or embeddable content parameter. |
| Memory-Management-Specification-v1.0.md | §14 | "Every unit of content entering Episodic, Semantic, or Long-term Memory is assigned an embedding at consolidation time (§16)... Index freshness: `EOS.VectorStore`'s index is updated synchronously with `EOS.KnowledgeGraph` writes for Episodic/Semantic/Long-term content — no eventual-consistency window is permitted between graph structure and its embedding for these permanent memory types." | Ties embedding generation exclusively to `consolidate()` (§16, WP-015). Does not define any lookup method, key, or read-path API on `EOS.VectorStore`. |
| Learning-Engine-Specification-v1.1.md | §11.2 | "candidates = Knowledge.query_similar(record.knowledge_graph_ref)     # excludes Quarantined records" followed by "similarity_results = ReasoningEngine.compare(record, candidates)" | Shows `query_similar()`'s return value (`candidates`) being passed to a separate, subsequent call (`ReasoningEngine.compare()`) that performs the actual similarity computation. |
| Learning-Engine-Specification-v1.1.md | §14.3 | "`IKnowledgeClient.query_similar()` (existing Constitutional interface, contract stated here for this consumer's benefit only — does not redefine §0.5.2)" — "Precondition (as consumed here): `ref` resolves to a non-Archived, non-Quarantined node" — "Postcondition (as consumed here): returned set never includes the querying record itself" | Documents `query_similar()`'s only real, already-approved consumer's expectations: a candidate-set precondition/postcondition, with no similarity-score expectation placed on `query_similar()` itself. |
| Memory-Management-Specification-v1.0.md | §5 (Non-Responsibilities) | "Semantic similarity computation / semantic comparison \| Reasoning Engine (forthcoming) \| Learning-Engine-Specification-v1.1 §7, §15" | States that Memory does not own similarity computation — consistent with that computation occurring in `ReasoningEngine.compare()`, not in `IKnowledgeClient`. |
| EOS-Implementation-Roadmap-v1.0.md | WP-014 row, "Objective" | "Implement the full seven memory-type Storage Strategy, hybrid symbolic+vector Retrieval Strategy, and the mechanical Retrieval Ranking formula." | Characterizes WP-014's objective as including a "hybrid symbolic+vector Retrieval Strategy." |
| EOS-Implementation-Roadmap-v1.0.md | WP-014 row, "Included components" | "the two-stage symbolic+vector retrieval algorithm" | Lists the two-stage algorithm as an included component of WP-014 specifically. |
| EOS-Implementation-Roadmap-v1.0.md | WP-014 row, "Prerequisites" | "WP-007, WP-011 (embedding channel)" | Lists WP-011's embedding channel as a prerequisite for WP-014. |
| EOS-Implementation-Roadmap-v1.0.md | WP-014 row, "Explicitly excluded" | "Context Assembly's budget/truncation logic (WP-015); Consolidation/Compression/Expiration (WP-015/WP-016)." | Excludes `assemble_context()` (§15) from WP-014's scope — the same method the detailed specification's component/sequence diagrams assign the vector stage to. |

## Findings

### `query()`

`query()`'s ratified signature — `query(MemoryType type?, string[] domain_tags?, DateRange range?)` (§20.1) — contains exactly three parameters: a closed memory-type enum, an array of domain tag strings, and a time range. None of these is, or can produce, an embedding. §13 stage 1 ("Symbolic stage: `EOS.KnowledgeGraph` resolves an initial candidate set via structured filters (memory type, `domain_tags`, node type per §0.5.1, time range)") maps onto these three parameters exactly. §13 stage 2 requires "a query embedding" as input to compute distance scores; no field in `query()`'s signature supplies one. This is a structural property of the signature itself, not an implementation choice: `query()` can only perform symbolic retrieval.

### `query_similar()`

`query_similar(KnowledgeGraphRef ref)` takes a single existing-node reference, never free text. Its one real, already-approved consumer — Learning-Engine-Specification-v1.1 §11.2's `ClusterTrigger.evaluate()` — calls it as `candidates = Knowledge.query_similar(record.knowledge_graph_ref)` and then passes `candidates` into a **separate** call, `ReasoningEngine.compare(record, candidates)`, which is what actually computes `similarity_results`. §14.3 states `query_similar()`'s precondition/postcondition purely in terms of node status and self-exclusion — never in terms of a similarity score. Memory-Management-Specification §5 independently confirms "Semantic similarity computation" belongs to the Reasoning Engine, not Memory. Together, these establish that `query_similar()`'s specified contract is to return a symbolic candidate pool; the similarity judgment happens afterward, elsewhere, in `ReasoningEngine.compare()`.

### Hybrid Retrieval

The specification's detailed architecture places the vector-retrieval stage at `ContextAssembler`/`assemble_context()`, not at `query()`/`query_similar()`. §9's component diagram wires `EOS.VectorStore` only to `ContextAssembler` and `LifecycleEngine` — never to any component associated with `query()`/`query_similar()`. §13 stage 3 names `ContextAssembler` explicitly as a participant in combining vector distance into the ranked list. §23.2's sequence diagram shows the vector-similarity call (`Memory->>VS: similarity(query_embedding, candidates)`) occurring inside the `assemble_context()` flow, as a step following and separate from a `query()` call that itself returns only "symbolic candidates." All three citations independently and consistently assign the vector stage to `assemble_context()` — a method the roadmap's own WP-014 row explicitly excludes ("Explicitly excluded | Context Assembly's budget/truncation logic (WP-015)").

## Roadmap Inconsistency

The roadmap's WP-014 row states its Objective includes a "hybrid symbolic+vector Retrieval Strategy," lists "the two-stage symbolic+vector retrieval algorithm" as an Included Component, and lists WP-011's embedding channel as a Prerequisite. Read in isolation, this wording suggests `query()`/`query_similar()` should themselves perform vector retrieval. The detailed Memory-Management-Specification's own component diagram (§9), retrieval-stage description (§13), and sequence diagram (§23.2) consistently assign the vector stage to `ContextAssembler`/`assemble_context()` — a method the same roadmap row explicitly excludes as WP-015's scope. These two characterizations, read together, describe incompatible assignments of responsibility for the same vector-retrieval stage. This report does not state that either document is wrong — only that their wording is inconsistent with each other on this specific point.

## Classification

Architecture Documentation Inconsistency

NOT:
- implementation defect
- architecture defect
- code defect

## Impact

WP-014's implementation remains compliant with the detailed specifications: `query()` implements symbolic retrieval exactly per §13 stage 1 and §20.1's literal signature; `query_similar()` implements a symbolic candidate pool exactly per Learning-Engine-Specification-v1.1 §11.2/§14.3's already-approved real usage; mechanical ranking (§19) is implemented for both. No production code changes are required as a result of this report. Future clarification, if desired, may be made by updating documentation only — it does not require, and should not trigger, any change to the current implementation.

## Recommendation

Clarify the roadmap's WP-014 row wording (its "Objective," "Included components," and "Prerequisites" fields) to state that WP-014 implements the *symbolic* half of Retrieval Strategy plus mechanical ranking, with the vector half completed by `ContextAssembler`/`assemble_context()` in WP-015 — bringing the roadmap's summary language into alignment with the detailed specification's already-consistent component assignment (§9, §13, §23.2).

Alternatively, record this as a standalone ADR documenting that the vector-retrieval stage of "hybrid symbolic+vector Retrieval Strategy" is owned by `ContextAssembler`/`assemble_context()` (WP-015) rather than by `IKnowledgeClient.query()`/`.query_similar()` (WP-014), without altering either document's existing text.

Either option is documentation-only and does not require any implementation change.

## Explicit Constraints

This report does not modify, and was not permitted to modify:
- `docs/EOS-Specification.md`
- `docs/Memory-Management-Specification-v1.0.md`
- `docs/Learning-Engine-Specification-v1.1.md`
- `docs/EOS-Implementation-Roadmap-v1.0.md`

No production code was changed. No files were staged. No commit was made. No push was made. No pull request was created.
