# ADR-010 — External Target Workspace Boundary and Initial SDK-Style .NET Project Profile

## 1. Status

**Accepted** — approved as the minimum architecture boundary for operating against one second, independent local software repository.

- **adr_id:** `ADR-010`
- **approver:** Human Architecture Authority / Product Owner, per Constitution Decision Matrix §0.6
- **conditions:** exactly one explicitly selected local Git Target Workspace per engineering invocation; one initial SDK-style .NET project profile; bounded Protection-governed discovery; Universal Gates 1–2 only; no mutation of the real Target Workspace; no plugin system, persistent workspace registry, new role, new provider, or expanded autonomy.

## 2. Context

The current composition root discovers the EOS installation repository by walking from the running assembly to `EOS.slnx`, loads that repository's `config/` directory, derives one `repositoryRoot` from it, and binds both `WorkspaceReader` and `IsolatedUniversalGateRunner` to that root. EOS installation/configuration identity and the engineering-target identity are therefore the same runtime value.

That binding is correct for the existing self-repository execution slice. Its `src/` and `tests/` path restriction, EOS solution/project naming rules, and `dotnet build`/`dotnet test` gate behavior protect and validate eos-core. They do not provide a safe or truthful way to select, inspect, or validate a second independent repository.

ADR-007, ADR-008, and ADR-009 remain accepted historical decisions. ADR-007 still requires applicability before artifact registration; for an external invocation, ADR-010 generalizes its fixed root to the immutable invocation Target Workspace. ADR-008's deterministic edit blocks and diff construction remain unchanged; ADR-010 replaces only its EOS-layout-specific eligible-path assumption for an external invocation. ADR-009 still requires Universal Gates 1–2 before `Running → Review`; ADR-010 supplies profile-driven equivalents for an external Target Workspace.

## 3. Decision

EOS may operate against exactly one explicitly selected local Git Target Workspace per engineering invocation. The HumanOperator selects it. EOS canonicalizes and validates it before engineering planning or execution and never silently substitutes or falls back to eos-core after an external workspace was requested.

The invocation binds planning, role execution, workspace access, patch applicability, isolated Universal Gates, and evidence presentation to one immutable Target Workspace descriptor. The real Target Workspace remains read-only. Candidate changes remain evidence for human review and may be applied only to an isolated validation copy by EOS.

### 3.1 Target Workspace

A **Target Workspace** is the one explicitly selected, canonical local Git working tree against which one EOS engineering invocation operates. It is distinct from the EOS installation/configuration repository.

The invocation-bound descriptor represents only:

- the canonical absolute root as runtime state;
- successful Git working-tree validation;
- the minimum non-secret audit identity;
- the selected project-profile identity; and
- the Protection scope for the invocation.

No persistent workspace registry, workspace database, durable workspace catalog, or multi-workspace model is introduced.

### 3.2 Workspace Selection

Selection belongs to the HumanOperator and must be explicit. EOS does not infer the engineering target from the current working directory, task text, EOS configuration discovery, or EOS binary location. Exact command-line syntax is an implementation and developer-experience decision, not part of this ADR.

An explicitly requested workspace that fails validation terminates preflight. EOS does not fall back to the EOS repository or any previously used target.

### 3.3 Canonicalization and Git Identity

Before planning or execution, the selected path must:

1. exist and be a directory;
2. be canonicalized to an absolute path;
3. resolve to the root of a valid local Git working tree;
4. be rejected if resolution is invalid or ambiguous; and
5. become immutable invocation state.

All target operations resolve relative to that canonical root. The raw absolute path need not be persisted. Durable audit correlation records only the minimum non-secret identity needed to associate evidence with the target, including a non-secret workspace identifier or fingerprint, the selected profile, and the base Git commit when available. Remote identity, if used, must be credential-safe. This ADR does not prescribe a storage representation.

For the initial external-workspace capability, the Target Workspace must also have a resolved Git `HEAD` and a clean, stable repository content state before engineering execution begins. Staged changes, modified tracked files, and untracked non-ignored files make the input state ambiguous and fail preflight. A detached `HEAD` is permitted when it resolves to a commit; an unborn `HEAD` is unsupported. Profile-approved ignored/generated restore assets may exist only where the profile explicitly permits them for isolated validation: they are not candidate source state and do not redefine the source-state identity.

EOS establishes one stable input-state identity before using source content. For this initial clean-workspace rule, the resolved `HEAD` commit plus successful clean-state and stability validation is that identity. A workspace-location identifier or fingerprint identifies the selected location; it is not proof of repository content. EOS must be able to detect a repository source-state change between preflight, source reads, patch applicability, and establishment of the isolated validation copy. Any such change fails closed without merge or reconciliation. Once established, the isolated copy is the immutable input for Gates 1–2. The mandatory invariant is:

`SeniorEngineer input state = patch applicability input state = isolated Gate input state`

This decision requires the invariant, not a general snapshot system or a particular implementation mechanism.

### 3.4 Protection Boundary

Workspace preflight, manifest discovery, content reads, candidate paths, patch applicability, and isolated-gate inputs are distinct Protection-governed operations scoped to the same canonical Target Workspace.

Every resolved path must fail closed unless it remains contained by that root. The following are forbidden:

- absolute candidate paths;
- traversal or alternate path forms that escape the root;
- following a symbolic link to content outside the root;
- Git administrative content, with `.git/` always excluded;
- protected secrets, including local credential material and secret-bearing environment files;
- binary-content inspection;
- generated, package, or build-output content inspection, except profile-required metadata explicitly allowed for preflight or isolated validation; and
- configuration reads not authorized by the selected profile and Protection scope.

Profile-required build metadata is a separate protected path class; permission to inspect it does not make it candidate-editable. Candidate evidence is initially restricted to profile-eligible, target-relative source and test text paths. Manifest discovery does not authorize reading the content of a manifest entry.

### 3.5 Bounded Discovery

One bounded workspace-manifest capability is authorized so EOS can inspect an unfamiliar supported project without requiring every path to appear in the task text beforehand. It must be:

- root-contained and non-recursive through symbolic links;
- deterministic for unchanged workspace state where practical;
- bounded by implementation/configuration ceilings and fail closed when they are exceeded;
- profile-aware and metadata-first; and
- excluding secrets, `.git`, binary content, packages, generated content, and build output except profile-required metadata.

Numeric ceilings remain implementation/configuration decisions. This decision does not authorize full repository indexing, content ingestion, embedding generation over the repository, background scanning, filesystem watching, or a durable workspace catalog.

### 3.6 Initial Supported Project Profile

Exactly one initial profile is authorized: **SDK-STYLE .NET GIT WORKSPACE**.

The profile supports SDK-style `.csproj` projects and either a `.sln`/`.slnx` solution or one unambiguous root project. Preflight verifies deterministic profile detection, required build metadata, and availability of a compatible installed .NET SDK. Dependency assets must already be available for offline isolated validation unless a separately approved architecture decision authorizes restore behavior.

Gate 1 and Gate 2 may execute only after the HumanOperator explicitly authorizes the selected workspace as trusted for local build/test execution. Selecting a filesystem path alone is not consent to execute repository-controlled code. Before gate execution, EOS must make visible that builds and tests may execute project-controlled code with host filesystem and network capabilities, that the isolated copy protects the real workspace from candidate mutation, and that this capability provides no operating-system sandbox. Exact confirmation or command-line interaction is an implementation decision.

The profile defines deterministic changed-file/project ownership, `dotnet build` as Gate 1, deterministically relevant `dotnet test` projects as Gate 2, and fail-closed behavior for unsupported or ambiguous repositories. Gate 2 is `NotApplicable` only when deterministic profile analysis proves that no test project is relevant to the affected production paths.

The profile must not depend on `EOS.slnx`, EOS naming, `src/P/P.csproj`, `tests/P.Tests/P.Tests.csproj`, or other EOS-specific directory names. Other languages and toolchains remain unsupported. Arbitrary shell-command profiles and a generic build-command framework are not authorized.

### 3.7 Universal Gates 1–2

For an external Target Workspace, EOS creates an isolated validation copy whose inputs are determined by the approved profile and Protection boundary. The candidate applies only inside that copy. The real workspace, index, and Git metadata remain unchanged.

Gate subprocesses run with the isolated target root as their working directory. Gate 1 uses the profile's approved build semantics. Gate 2 uses only deterministically relevant profile tests. Unsupported or ambiguous targets, missing toolchains or inputs, copy or process failures, timeouts, and indeterminate results fail closed. Gates 3–5 remain unimplemented.

The existing `IUniversalGateClient` architecture remains sufficient. This decision introduces neither a new Gate subsystem nor a new decision engine.

### 3.8 Planning and Execution Binding

One invocation owns one immutable Target Workspace descriptor. The same descriptor binds:

`Target Workspace → current invocation → current Plan → eligible current tasks → SeniorEngineer → workspace access → patch applicability → Universal Gates → evidence presentation`

No component may rediscover, infer, or substitute the target, invocation, Plan, or task association. For an external Target Workspace invocation, EOS may dispatch only tasks proven to belong to the Plan/current engineering work created for that same invocation. A Ready task from an earlier invocation, another Plan or Goal, or an unknown invocation context is ineligible and must not inherit the current Target Workspace authority. If an external-workspace task cannot complete within its authorized invocation, it must not remain eligible for dispatch under an unrelated future external-workspace invocation.

The execution path retains an explicit association between the invocation and its eligible Plan/tasks and restricts dispatch to it. Existing Plan and Task identity plus composition-root invocation context may satisfy this rule; this decision does not add Target Workspace fields to Goal, Plan, PlanTask, or Task schemas and introduces no lifecycle state. Persisted or concurrent multi-workspace execution remains outside this decision and requires future architecture before tasks may outlive their workspace binding.

### 3.9 Evidence and Audit

The exact unified diff remains an immutable Artifact Registry artifact and its bytes are not modified to embed workspace metadata. No second evidence store is introduced.

The human review path must correlate:

- the exact candidate artifact and affected target-relative paths;
- the minimum non-secret workspace identity;
- the resolved base Git `HEAD` commit and successful clean/stability validation that identify the source content state;
- the selected project profile;
- the Gate 1 result; and
- the Gate 2 result.

The selected canonical root is visible to the HumanOperator during the invocation, and the exact candidate diff and gate results must be inspectable. The implementation may use existing artifact, task, and event audit paths; this ADR does not prescribe storage mechanics.

### 3.10 Real Workspace Immutability

EOS does not modify the selected real Target Workspace. It must not apply the candidate there, write source files, stage Git changes, create a branch, commit, push, perform PR operations, or modify Git metadata. Human review remains authoritative over whether and how candidate evidence is applied.

### 3.11 Security Limitation

The isolated validation copy is **not an operating-system security sandbox**. External MSBuild targets and tests execute project-controlled code with the capabilities of the host process. EOS must scrub its own runtime credentials and environment according to the established gate boundary, but this ADR makes no host-level isolation claim. Stronger sandboxing is outside this decision.

## 4. Consequences

- EOS can be implemented to run its existing candidate-engineering workflow against one independent supported .NET project without confusing that project with the EOS installation.
- Workspace access and gate execution become invocation-target-bound and profile-aware while retaining existing contract and composition-root boundaries.
- Bounded manifest discovery becomes a real, separately protected capability.
- Unsupported, ambiguous, or insufficiently prepared projects fail during preflight or validation rather than falling through to eos-core.
- The real workspace remains unchanged and the human remains responsible for applying candidate evidence.
- External project build targets and tests remain capable of using host process permissions; isolation is not represented as sandboxing.

## 5. Risks

- Incorrect canonicalization or symbolic-link handling could expose paths outside the target.
- External build targets and tests execute project-controlled code with host process capabilities.
- Secret classification may not recognize every sensitive file.
- Project ownership or relevant-test resolution may be ambiguous.
- Invocation-only binding would be insufficient if tasks were later allowed to outlive the process or execute concurrently across workspaces.
- The selected repository could contain project-controlled build or test logic the HumanOperator does not trust to execute locally.

Each uncertainty fails closed within this decision's scope. Stronger host isolation, improved secret classification, and persisted/concurrent workspace identity require separate evidence and authorization.

## 6. Alternatives Rejected

- **Continue using the eos-core root:** cannot operate against a second project.
- **Infer the target from the current working directory:** permits silent accidental target selection.
- **Use persistent global workspace configuration:** creates unnecessary state and stale-target risk for a one-invocation capability.
- **Add workspace identity to every Goal, Plan, and Task now:** duplicates immutable invocation state without a current persisted/concurrent consumer.
- **Retain explicit-path-only inspection:** cannot realistically inspect an unfamiliar project.
- **Allow unrestricted recursive crawling:** is unbounded and risks secret or irrelevant-content collection.
- **Reuse EOS's `src/`/`tests/` layout rules:** falsely constrains independent repositories to eos-core conventions.
- **Accept arbitrary shell-command profiles:** creates a generic execution framework and unnecessary command authority.
- **Introduce plugins:** no second profile or runtime extension consumer exists.
- **Support multiple languages now:** exceeds current implementation evidence and the minimum adoption target.
- **Mutate the real workspace:** violates the established candidate-evidence and human-review boundary.
- **Amend the Constitution:** unnecessary because EOS solution structure, subsystem dependencies, role authority, task lifecycle, and autonomy boundaries do not change.

## 7. Explicit Non-Goals

- real Target Workspace mutation or candidate application;
- Git staging, branch creation, commit, push, or PR operations;
- Universal Gates 3–5;
- automatic `Review → Testing → Verified` advancement;
- automatic retry, repair, or rollback orchestration;
- multiple workspaces per invocation or concurrent multi-workspace execution;
- a persistent workspace registry, database, or catalog;
- cloud workspaces or automatic cloning;
- a generic toolchain or arbitrary-command framework;
- plugin discovery or a plugin ecosystem;
- new AI providers or autonomous roles; and
- a new Work Package.

## 8. KISS/YAGNI

One explicitly selected workspace, one immutable invocation binding, one bounded manifest, one SDK-style .NET profile, the existing contracts, and Universal Gates 1–2 are sufficient to prove second-project adoption. Registries, persistent project state, plugins, multiple profiles, new subsystems, and additional autonomy have no current consumer and are excluded.

## 9. Acceptance Criteria for Future Implementation

This ADR records requirements, not claims of completed implementation. A future implementation must prove that:

1. A HumanOperator explicitly selects one independent local Git Target Workspace.
2. EOS displays the canonical selected target and records sufficient non-secret audit identity.
3. The target has a resolved `HEAD`, is clean of staged, modified tracked, and untracked non-ignored files, and remains stable until the isolated validation copy is established; detached `HEAD` is permitted when resolvable and unborn `HEAD` is rejected.
4. SeniorEngineer reads, patch applicability, and the isolated Gate copy use the same validated source content state, which is correlated with the candidate evidence.
5. EOS never silently substitutes eos-core after an external workspace was requested.
6. Discovery is bounded, root-contained, profile-aware, and Protection-governed.
7. Candidate and read paths cannot escape the target through traversal or symbolic links.
8. `.git`, protected secrets, binaries, packages, and generated/build output are excluded according to this decision.
9. The real target files, Git index, and Git metadata remain unchanged.
10. Planning, SeniorEngineer, workspace access, patch applicability, Universal Gates, and evidence presentation use the same immutable descriptor.
11. Only tasks associated with the current external-workspace invocation and its Plan are eligible for dispatch; unrelated persisted tasks cannot inherit its workspace authority.
12. `git apply --check` operates against the selected Target Workspace.
13. Gates 1–2 execute only in an isolated copy of that target and only after explicit HumanOperator authorization to execute trusted project-controlled build/test code.
14. One non-EOS-named SDK-style .NET Git workspace completes the workflow.
15. Unsupported or ambiguous project types fail closed during preflight.
16. The human can inspect the exact candidate diff, affected paths, source-state identity, and Gate 1–2 results.
17. No real-workspace mutation, commit, push, PR operation, Gate 3–5 implementation, plugin framework, or expanded autonomy is introduced.
