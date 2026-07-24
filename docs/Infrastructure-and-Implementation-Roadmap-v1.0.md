# Infrastructure and Implementation Roadmap v1.0

**Document Type:** Implementation Roadmap (not an architecture specification)
**Basis:** All ten approved, frozen EOS architecture specifications, validated in `Architecture-Validation-Report-v1.0.md`
**Target Machine:** Dell Laptop, Intel i7-1065G7, 32GB RAM, 512GB NVMe SSD, Ubuntu 24.04 LTS, offline-first, single developer workstation

This document does not redesign, question, or extend the frozen architecture. It answers one question only: **in what order, with what tools, and with what checkpoints do you go from an empty laptop to a running EOS development environment?** Where the Architecture Validation Report identified an open blocker (project registration, configuration schema, hardware capacity validation, missing Dashboard/Pipeline specs), this roadmap notes where that blocker is practically addressed as part of the build sequence — it does not resolve those blockers architecturally, only operationally, where the two happen to coincide (most notably: Phase 3 and Phase 7 are where the hardware-capacity blocker gets its first real evidence).

## How to Use This Document

Each phase is independently completable and independently verifiable — do not start a phase until the prior phase's Validation Checklist passes in full. Every phase includes a Rollback Strategy because the Implementation Principles require every step to be reversible; treat rollback instructions as load-bearing, not optional reading. Estimated durations assume one developer working at a sustainable pace, not continuous effort — they are planning inputs, not deadlines.

---

## Phase 1 — Prepare the Laptop

### Goal

A clean, fully updated, reliably bootable Ubuntu 24.04 LTS installation with a disk layout simple enough to recover from without data-recovery tooling.

### Tasks

1. **BIOS checks (only if required).** Enter BIOS/UEFI setup and confirm: Intel VT-x/VT-d virtualization is enabled (required for reasonable Docker performance later); Secure Boot may remain enabled (Ubuntu 24.04 supports it natively — do not disable unless a specific driver conflict demands it); disable Fast Boot if it interferes with USB installer boot.
2. **Ubuntu 24.04 LTS installation.** Download the official Ubuntu 24.04 LTS ISO on any machine with internet access, verify its SHA256 checksum against the published value, write it to USB (e.g., via `dd` or a tool like Rufus/Balena Etcher from another machine), and install using the standard installer.
3. **Disk layout.** Keep it simple, deliberately:
   - `/boot/efi` — 512MB (EFI System Partition, standard)
   - `/` (root) — remainder of the 512GB NVMe, single ext4 partition, no LVM
   - Swap — a 16GB swapfile (not a partition) created post-install, sized generously because local LLM inference and multiple concurrent data stores (Phase 3) will pressure RAM
   - **Explicitly avoid LVM, ZFS, or Btrfs** for this bootstrap — none of the frozen architecture requires snapshotting filesystems, and a plain ext4 layout is trivially understood by any recovery tool, which matters more than the marginal benefit of a filesystem-level snapshot feature you are not otherwise using.
4. **User account setup.** Create a single non-root sudo-capable user; set a hostname (e.g., `eos-dev`); generate an SSH key pair (`ed25519`) for later Git/GitHub authentication even though the system is offline-first day to day — you will need connectivity for the one-time setup steps in Phases 2–3.
5. **System updates.** `sudo apt update && sudo apt full-upgrade -y`, then reboot once to apply any kernel update before proceeding.

### Expected Deliverables

- A booted, updated Ubuntu 24.04 LTS system with a single sudo user, a 16GB swapfile active, and no pending `apt` upgrades.

### Estimated Duration

1–2 hours (excluding ISO download time).

### Validation Checklist

- [ ] `lsb_release -a` reports Ubuntu 24.04 LTS
- [ ] `df -h /` shows the expected root partition size with no separate LVM volumes
- [ ] `swapon --show` shows the 16GB swapfile active
- [ ] `sudo apt update` completes with no errors
- [ ] `whoami` and `groups` confirm the created user has `sudo`
- [ ] System reboots cleanly with no boot warnings

### Common Mistakes

- Enabling LVM "just in case" — adds recovery complexity with no benefit this architecture actually uses.
- Skipping or under-sizing swap, then hitting out-of-memory kills once Phase 3's local LLM and data stores run concurrently.
- Leaving virtualization disabled in BIOS, causing silently degraded Docker performance discovered only much later in Phase 3.
- Using a swap partition instead of a swap file, making later resizing require a repartition instead of a one-line `fallocate`/`resize`.

### Rollback Strategy

At this stage rollback is trivial and cheap: nothing valuable has been created yet. Re-run the Ubuntu installer from the same verified USB image and start over. Keep the verified ISO and its checksum on a second USB drive or another machine so a full reinstall is always a 20-minute operation, not a research project.
## Phase 2 — Development Environment

### Goal

Every tool the frozen architecture's implementation will actually require — no more — installed, verified, and configured for offline-capable daily use.

### Tasks

1. **Git.** `sudo apt install git` — configure `user.name`/`user.email` and generate/import the SSH key from Phase 1 for authentication.
2. **GitHub CLI.** Install `gh` via the official apt repository (not `snap`, to avoid snap's sandboxing quirks with local file access later); run `gh auth login` once while online.
3. **VS Code.** Install via the official Microsoft apt repository (`.deb`), not the Ubuntu Software snap build, for more predictable extension/file-access behavior with Docker-mounted volumes.
4. **Docker Engine + Docker Compose.** Install Docker Engine from Docker's official apt repository (not Ubuntu's bundled `docker.io` package, which lags in version); install the `docker-compose-plugin` (Compose v2, invoked as `docker compose`, not the legacy standalone `docker-compose` v1 binary); add the user to the `docker` group and re-log in.
5. **Python.** Use the Ubuntu 24.04 system Python 3.12 plus the standard `venv` module for any Python tooling needs — do **not** introduce `pyenv`, `conda`, or `poetry` unless a specific frozen-architecture requirement demands a different Python version than what Ubuntu ships (none currently do). This is a deliberate simplicity choice: one Python, one way to make a virtual environment.
6. **Node.js (LTS).** Install via `nvm` (Node Version Manager) rather than `apt`'s Node package — `nvm` avoids version drift against Ubuntu's release cadence and is the simplest way to pin exactly the LTS version the frontend/tooling needs. Install one LTS version and set it as default; do not install multiple Node versions unless a concrete need arises.
7. **Bun — not installed, and this is a deliberate decision, not an oversight.** No frozen specification names Bun as a requirement, and the Implementation Principles state "do not introduce unnecessary technologies." Node LTS alone is sufficient for everything the architecture currently calls for. Revisit only if a specific future subsystem's implementation genuinely benefits from it — do not add it speculatively.
8. **.NET SDK (latest stable).** Install via Microsoft's official apt repository — the Constitution's own solution structure (`EOS.sln` and its constituent projects) is a .NET solution, so this is the primary application runtime.
9. **Build tools.** `sudo apt install build-essential` plus any headers Docker/Python native extensions may need (`python3-dev`) — installed now so nothing blocks Phase 3/4 mid-task.

### Expected Deliverables

A machine where `git`, `gh`, `code`, `docker`, `docker compose version`, `python3`, `node`, and `dotnet` all run successfully for the current user without `sudo`, and VS Code is configured with, at minimum, the C# Dev Kit and Docker extensions.

### Estimated Duration

2–3 hours.

### Validation Checklist

- [ ] `git --version`, `gh --version`, `code --version` all succeed
- [ ] `docker run hello-world` succeeds **without `sudo`**
- [ ] `docker compose version` reports Compose v2 (not a v1 standalone binary)
- [ ] `python3 --version` reports 3.12.x; `python3 -m venv test-env && source test-env/bin/activate && deactivate` succeeds
- [ ] `node --version` reports the intended LTS line; `nvm list` shows exactly one installed version unless a second was deliberately added
- [ ] `dotnet --version` reports the latest stable SDK; `dotnet new console -o /tmp/hello && dotnet run --project /tmp/hello` prints output
- [ ] VS Code opens and both the C# and Docker extensions are active

### Common Mistakes

- Forgetting to log out/in (or `newgrp docker`) after adding the user to the `docker` group, then "fixing" the resulting permission error by running Docker with `sudo` from then on — this silently reintroduces root-owned files in bind-mounted volumes later.
- Installing Node via both `apt` and `nvm`, creating a `PATH` conflict where `which node` resolves inconsistently across shells.
- Installing the legacy standalone `docker-compose` (v1) alongside the v2 plugin, then mixing `docker-compose` and `docker compose` invocations across scripts.
- Adding Bun, `pyenv`, or `conda` "for flexibility" before any concrete requirement exists — each is one more thing to keep updated and explain in onboarding, for zero present benefit.

### Rollback Strategy

Every tool in this phase is an independent, cleanly-uninstallable package (`sudo apt remove --purge <package>`, or `nvm uninstall <version>`, or deleting `~/.dotnet`). No tool in this phase touches project data, so rollback is always "uninstall and reinstall," never "restore from backup." If Docker itself becomes unstable, `sudo apt purge docker-ce docker-ce-cli containerd.io docker-compose-plugin` followed by a clean reinstall resolves the overwhelming majority of issues without needing to revisit Phase 1.
## Phase 3 — Core Infrastructure

### Goal

A locally-running, offline-capable AI inference runtime and data-store stack, sized to fit the target hardware — and the first real evidence toward closing the Architecture Validation Report's hardware-capacity blocker (Blocker #3 in that document).

### Tasks

1. **Install Ollama** as the local inference runtime. Justification: Ollama is the most proven, low-friction way to run quantized local LLMs on Ubuntu without hand-rolling a model-serving stack; it exposes a stable local REST API that maps cleanly onto AI Provider Layer's provider-abstraction needs (a Provider Adapter can be written once against Ollama's API), and it manages model downloads/quantization automatically. Install via the official install script (`curl -fsSL https://ollama.com/install.sh | sh`), which sets it up as a systemd service.
2. **Pull a Qwen model — start small, deliberately.** Given the i7-1065G7 has no dedicated GPU, start with a heavily quantized, smaller-parameter Qwen variant (e.g., a 4-bit-quantized ~7B-class build) rather than the largest available Qwen model. This is the single most important practical decision in this phase: it is where the Architecture Validation Report's hardware-capacity concern stops being theoretical and starts being measured (see Validation Checklist below). If the smaller model performs acceptably with headroom to spare, scaling up is a one-line `ollama pull` away later (Phase 9); if it does not, you have learned this on day one instead of after building four subsystems on top of an infeasible assumption.
3. **Install ChromaDB via Docker**, not native pip install — running it as `chromadb/chroma` in a container with a bind-mounted data volume keeps it fully reversible (`docker compose down -v` wipes it cleanly) and isolates its Python dependency chain from your own tooling (Phase 2).
4. **Required databases, per the frozen Data Architecture.** SQL Server and Redis are both named in the frozen architecture (Constitution Part 4) — run both as Docker containers (`mcr.microsoft.com/mssql/server:2022-latest` and `redis:7-alpine`), each with a bind-mounted data volume. SQLite requires no separate installation — it ships as a library consumed directly by .NET and is used only for local/offline-cache scenarios per the frozen architecture, so there is nothing to provision here beyond ensuring the data directory (below) exists.
5. **Local storage layout.** Create a single, predictable directory tree, e.g. `~/eos/data/{sql,redis,chroma,artifacts,logs,backups}`, and point every container's bind mount and every local config value at paths under it — this one convention is what makes Phase 8's backup strategy trivial later.

### Expected Deliverables

Ollama running as a service with one Qwen model available; ChromaDB, SQL Server, and Redis running as Docker containers with persistent volumes under `~/eos/data`; a recorded baseline measurement of RAM/CPU headroom with the model loaded and idle, and under a single concurrent request.

### Estimated Duration

3–5 hours (variable, dominated by model download time — this is the one phase where a slow or metered connection matters, since everything after this point is designed to run offline).

### Validation Checklist

- [ ] `ollama list` shows the pulled Qwen model
- [ ] `curl http://localhost:11434/api/generate -d '{"model":"<model>","prompt":"hello"}'` returns a completion
- [ ] ChromaDB's heartbeat endpoint (`curl http://localhost:8000/api/v1/heartbeat`) responds
- [ ] `docker ps` shows SQL Server, Redis, and ChromaDB all in a healthy state
- [ ] A basic SQL connection (`sqlcmd`/`mssql-cli`) and `redis-cli ping` both succeed against the running containers
- [ ] **Record actual measurements:** `free -h` and CPU load with (a) all containers running and idle, (b) the Qwen model loaded and idle, and (c) one active inference request in flight. Write these three numbers down — they are the first empirical data point against Resource Management's own thresholds, which that specification itself states are unvalidated estimates.
- [ ] Disconnect networking entirely and confirm Ollama, ChromaDB, SQL Server, and Redis all continue to operate normally (the offline-first validation this architecture depends on)

### Common Mistakes

- Pulling the largest available Qwen variant "to get the best quality" before measuring whether a smaller one already fits comfortably — this is the single most likely way to rediscover the Architecture Validation Report's High-severity hardware risk the hard way, mid-Phase-5, instead of now.
- Running ChromaDB via a native `pip install` instead of Docker, entangling its dependencies with your Python tooling environment from Phase 2.
- Forgetting bind-mounted volumes on the SQL Server/Redis/ChromaDB containers, then losing all data the first time a container is recreated.
- Not recording the baseline resource measurements — the whole point of this phase, per the Validation Report, is to generate evidence, not just to get things running.

### Rollback Strategy

`docker compose down -v` (once Phase 4 consolidates these into one Compose file) removes every container and volume cleanly, returning to a blank slate. `ollama rm <model>` removes a pulled model without touching anything else. Because every service here is independently containerized (except Ollama, which is a systemd service with its own clean `apt`/script-based uninstall), any single component can be reset without affecting the others — if only ChromaDB is misbehaving, only ChromaDB needs to be torn down and recreated.
## Phase 4 — EOS Project Bootstrap

### Goal

A repository and solution structure that mirrors the frozen Constitution's Physical Repository Architecture exactly, a working configuration/logging/secrets convention, and a single `docker compose up` that brings up the entire infrastructure stack reproducibly — with the EOS process itself starting and reaching "Ready" (Constitution Part 12's Bootstrap sequence) even though almost every subsystem is still a stub.

### Tasks

1. **Repository structure.** `git init`; create the top-level layout the Constitution's Physical Repository Architecture already specifies (`src/`, `tests/`, `benchmarks/`, `docs/`, `scripts/`, `deploy/`, `prompts/`); add a `.gitignore` covering .NET build output, Node `node_modules`, Python venvs, and — critically — `~/eos/data` equivalents if any local data path is ever placed inside the repo tree (it should not be; keep data under `~/eos/data`, outside the git-tracked tree entirely, per Phase 3's layout).
2. **Solution structure.** Create `EOS.sln` and scaffold every project the Constitution's solution structure names (`EOS.Core`, `EOS.SharedKernel`, `EOS.Contracts`, `EOS.Domain`, `EOS.Application`, `EOS.Infrastructure`, `EOS.Orchestrator`, `EOS.Planner`, the role projects, `EOS.Knowledge`, `EOS.KnowledgeGraph`, `EOS.VectorStore`, `EOS.Gates`, `EOS.Pipeline`, `EOS.SDK`, `EOS.Dashboard`, `EOS.Web`, `EOS.Mobile`, `EOS.Tools`, `EOS.Runner`) **plus the four new projects the approved subsystem specifications introduced** (`EOS.Learning`, `EOS.Reasoning`, `EOS.AIProvider`, `EOS.Resources`) — each as an empty class library/executable scaffold with only the dependency references the frozen Module Dependency Rules permit. This is also the practical moment to close the Architecture Validation Report's Blocker #1: **before scaffolding, update the Constitution's own Part 1 table** with the consolidated registration the System Architecture Specification's ADR-SYS001 already proposed, so the solution you build matches a document that actually describes it.
3. **Configuration strategy.** Create the ten configuration files the Constitution's Part 10 names (`EOS.json`, `Planner.json`, `Inference.json`, `Providers.json`, `Thresholds.json`, `Security.json`, `Dashboard.json`, `Knowledge.json`, `Storage.json`, `FeatureFlags.json`) as real files with a minimal, genuinely-schematized set of fields — this is also the practical place to begin closing Blocker #2 (no configuration schema existed anywhere in the specifications). Even a minimal first-pass schema, documented in `docs/`, is strictly better than the current state of "every subsystem references this file by name with no defined shape."
4. **Environment variables.** A single `.env.example` at the repo root documenting every variable the Compose stack and the .NET configuration system need (connection strings, Ollama endpoint, ChromaDB endpoint) — never commit the real `.env`.
5. **Logging.** Configure a simple, working logging pipeline now — Serilog (or the .NET built-in `ILogger` with a console+rolling-file sink) writing to `~/eos/data/logs`. **Deliberately defer full OpenObserve deployment** to a later phase (Phase 6 onward): OpenObserve is a real piece of infrastructure with its own resource footprint, and getting *some* structured logging working today is more valuable than blocking on the full observability stack before you have anything worth observing.
6. **Secrets management.** For a single-developer offline workstation, `dotnet user-secrets` for local development plus a `.env` file (git-ignored) is sufficient — do not introduce a dedicated secrets-manager service (Vault, etc.) at this stage; nothing in the frozen architecture requires one for a single-workstation deployment, and doing so would violate "avoid over-engineering."
7. **Initial Docker Compose.** Consolidate Phase 3's ad hoc containers into one `docker-compose.yml` at the repo root, with explicit `depends_on` + healthchecks (SQL Server in particular needs a real healthcheck, not just a port check, since it takes tens of seconds to become ready) so `docker compose up` is reliably reproducible from a clean checkout.

### Expected Deliverables

`dotnet build` succeeds across the full solution skeleton with zero errors; `docker compose up` brings up SQL Server, Redis, and ChromaDB (Ollama remains a host-level systemd service, not containerized, per Phase 3's choice) all reporting healthy; running `EOS.Runner` starts, executes Constitution Part 12's Bootstrap sequence against stub subsystems, and logs a "Ready" state.

### Estimated Duration

1–2 days.

### Validation Checklist

- [ ] Every project named in the (now-updated) Constitution Part 1 table exists in `EOS.sln` and builds
- [ ] No project violates a Module Dependency Rule (spot-check a few: `EOS.SeniorEngineer` does not reference `EOS.CTO`; `EOS.Dashboard` references only `EOS.Contracts`)
- [ ] `docker compose up -d` brings up all three containerized services healthy from a clean checkout on a machine that has never run them before
- [ ] `EOS.Runner` starts, logs each Bootstrap step (Install → Validate → ... → Ready per Constitution Part 12), and reaches Ready without crashing
- [ ] Logs appear under `~/eos/data/logs` in a readable format
- [ ] `.env` is confirmed absent from `git log` history (`git log --all --full-history -- .env` returns nothing)
- [ ] Configuration files parse without error and match the minimal schema documented in `docs/`

### Common Mistakes

- Scaffolding all projects with unrestricted references "to save time later," silently violating the frozen dependency rules from day one — cheap to avoid now, expensive to unwind once real code exists in every project.
- Skipping the SQL Server container healthcheck, causing intermittent Compose startup races that look like application bugs.
- Committing a real `.env` once, even briefly — `git filter-repo` cleanup afterward is disruptive; simply never commit it in the first place.
- Trying to stand up the full OpenObserve observability stack before anything exists worth observing, burning a day of infrastructure work with no immediate payoff.

### Rollback Strategy

Everything in this phase lives in git and Docker volumes. To roll back the solution structure, revert to the last known-good commit. To roll back infrastructure, `docker compose down -v` and re-run `docker compose up -d` from the committed `docker-compose.yml`. Because Phase 3's data directories are separate from the git tree, a bad application-level change never risks the underlying data stores, and a bad infrastructure change never risks source history.
## Phase 5 — First Vertical Slice

### Goal

Prove the smallest possible end-to-end path through the real architecture — User Request → AI Provider → Reasoning → Memory → Response — before building any subsystem out in full. This is deliberately a thin slice, not a prototype to be thrown away: it is built against the real infrastructure from Phases 3–4, using the real interfaces the frozen specifications define, just with minimal logic behind each one.

### Tasks

1. **Entry point.** A minimal console command (e.g., `eos ask "<question>"`) that accepts free text and constructs the simplest possible request object — standing in for a full `IPlanningClient.submit_goal()` call, deferred to Phase 6 once Planning & Execution Engine is real.
2. **Minimal AI Provider Layer path.** Implement just enough of `IAIProviderClient` and a single Provider Adapter targeting Ollama (Phase 3) to satisfy `infer()` — skip the full Provider/Model Registry, Health Monitoring, and Failover for now (Phase 6 builds those out); a single hardcoded provider binding is acceptable at this stage as long as it is exposed behind the real, frozen interface signature, not a throwaway ad hoc method.
3. **Minimal Reasoning Engine path.** Implement enough of the 12-stage pipeline to produce a well-formed `Decision` — Context Processing, a pass-through Goal Understanding, a single-hypothesis Decision Making step, and Explainability populated with real evidence references — explicitly skipping the richer reasoning types, Alternative Exploration depth, and Trade-off Analysis nuance the full specification describes. The point is a real, schema-valid `Decision` object, not a sophisticated one.
4. **Minimal Memory Management path.** Write the interaction to the real SQL Server instance (Phase 3) using the actual node schema Memory-Management-Specification-v1.0 defines (`KnowledgeNode` with `node_type=Fact` or a similar minimal classification), rather than a throwaway table — this is the one place where building against the real schema from day one, instead of a prototype schema you'll discard, pays for itself the first time Phase 6 extends it.
5. **Explicitly deferred for this phase:** Protection Layer (use an always-Allow stub — see the note below), Learning Engine, Knowledge Management, Planning & Execution Engine's full Goal/Task machinery, Resource Management's real capacity computation (hardcode a generous static budget for now).

**A note on the Protection Layer stub:** even though Protection is deferred to full implementation in Phase 6, do not skip calling `IProtectionClient.validate()` in this slice — implement it as a stub that always returns Allow, but wire the call into the real place the architecture requires it. This costs almost nothing now and means Phase 6's full Protection Layer implementation is a drop-in replacement of the stub's internals, not a retrofit of a call path that was never there.

### Expected Deliverables

Running `eos ask "explain the SOLID principles"` returns a locally-generated, coherent response, and the interaction is persisted as a real `KnowledgeNode` row in the real SQL Server instance.

### Estimated Duration

3–5 days.

### Validation Checklist

- [ ] The command round-trips end to end with no manual intervention
- [ ] **Disconnect Wi-Fi/Ethernet entirely and re-run the same request** — it must succeed identically, proving the offline-first guarantee is real for this slice, not assumed
- [ ] Measure and record wall-clock latency for a representative request — this is the second, more realistic data point (after Phase 3's raw inference benchmark) toward closing the Architecture Validation Report's hardware-capacity blocker, since it now includes real orchestration overhead, not just raw model latency
- [ ] Confirm the interaction is queryable directly in SQL Server as a real `KnowledgeNode` row, not a log line
- [ ] Feed a malformed/empty request and confirm a clean, structured failure rather than a crash
- [ ] Confirm the `IProtectionClient.validate()` stub call actually executes (e.g., via a log line) on every request, not just in code review

### Common Mistakes

- Building more of the full Reasoning/Planning pipeline than this phase needs "while you're in there" — this phase's entire value is proving the thin slice works before investing further; scope creep here delays the first real feedback signal the whole roadmap depends on.
- Skipping the Protection stub call entirely "since it's a no-op anyway" — this is exactly the shortcut that makes Phase 6's real Protection Layer integration a retrofit instead of a swap.
- Not testing offline — the easiest validation in this entire roadmap to skip, and the single most important one given how central "offline-first" is to every frozen specification.
- Writing to a throwaway/simplified data schema instead of the real one, creating migration work in Phase 6 that a small amount of extra care now would have avoided.

### Rollback Strategy

This slice lives entirely in application code on a feature branch and writes only to the real (already-reversible, per Phase 3) SQL Server volume. If the slice's approach proves wrong, `git revert`/branch deletion removes the code; `docker compose down -v` on the SQL Server volume alone (or a targeted `DELETE` of the test rows) removes the data without touching Phases 1–4's infrastructure.
## Phase 6 — Progressive Implementation

### Goal

Establish the order in which the remaining subsystems should be built out from their Phase 5 stubs to their full, frozen-specification behavior — and explain why that order, not another one. **This phase is guidance only; nothing here is implemented as part of this roadmap.**

### Recommended Build Order and Rationale

1. **Resource Management (from stub to real).** Phase 5 hardcoded a static budget. Nearly every other subsystem (Planning & Execution Engine's Scheduler, Protection Layer's Resource Validation, AI Provider Layer's routing) consumes Resource Management's published values — building it real early means every subsequent subsystem is built against real numbers instead of a placeholder that later needs to be swapped out from underneath them. This is also where the Architecture Validation Report's hardware-capacity question gets its most rigorous answer yet, since Resource Management's own thresholds can now be calibrated against Phase 3/5's already-recorded measurements.
2. **AI Provider Layer (from single-adapter stub to full).** Build out the real Provider/Model Registry, Health Monitoring, and Failover — still targeting only Ollama/Qwen for now (Phase 9 covers adding providers/models). This is foundational to everything cognitive that follows.
3. **Memory Management (from minimal write path to full).** Implement the full seven memory-type lifecycle, Context Assembly, and mechanical ranking — Reasoning Engine's real implementation (next) depends on `assemble_context()` actually working as specified, not as Phase 5's placeholder write path.
4. **Protection Layer (from always-Allow stub to full).** This comes earlier than a naive "build what's easiest first" ordering would suggest, and deliberately so: every subsequent subsystem's real behavior includes real risk, and running real autonomous logic behind a permissive stub for longer than necessary is itself a risk this roadmap does not recommend accepting. Build the full tiered Validation Pipeline now, before Reasoning Engine's richer decision-making and Learning Engine's autonomous promotion logic come online.
5. **Reasoning Engine (from minimal pipeline to full 12-stage).** Now that Memory and Protection are real, build out the full reasoning-type catalog, Alternative Exploration, Trade-off Analysis, and Explainability depth.
6. **Knowledge Management (layered onto the now-real Memory Management).** Taxonomy, relationships, quality/governance/freshness metadata, and the additive search ranking pass.
7. **Learning Engine.** Depends on Memory (real), Reasoning (real, for `compare()`/`get_trust_signal()`), and benefits from Knowledge Management already existing to classify its promotion outputs — hence it comes after all three rather than in parallel with them.
8. **Planning & Execution Engine (from Phase 5's absent Goal/Task machinery to full).** Depends on Reasoning (for bounded delegation), Protection (for dispatch gating), and Resource Management (for budget values) all being real — building it earlier would mean building against stubs for all three of its own dependencies simultaneously.
9. **Autonomous Engineering Loop.** Last, deliberately — it is purely an orchestration layer over the other eight, and building it before at least most of them are real would produce a sequencer with nothing real to sequence.
10. **`EOS.Dashboard` and `EOS.Pipeline`.** The Architecture Validation Report flagged both as registered-but-unspecified. Commission a lightweight specification for each **before or alongside** this phase's work (not after) — Dashboard in particular becomes far more useful the earlier it can render real subsystem events, so a minimal Dashboard implementation tracking alongside steps 3–7 above (rather than bolted on at the very end) is a reasonable adjustment to this ordering if observability during the build itself is a priority.

### Expected Deliverables

None — this phase is a decision record, not a build phase. Its deliverable is the ordering above, to be followed by future implementation work.

### Estimated Duration

Not applicable to this phase itself; each numbered item above is its own multi-week implementation effort once undertaken.

### Validation Checklist

- [ ] Before starting each numbered item, confirm every dependency it lists as "now real" has actually passed its own Phase 7-style validation (see next phase) — do not begin an item on the assumption that a dependency is done when it has only been started.

### Common Mistakes

- Building subsystems in parallel across multiple items "for speed," reintroducing the stub-swap risk this ordering exists to avoid.
- Treating Protection Layer as low-priority because it was "just a stub that worked fine" in Phase 5 — the longer real autonomous behavior runs behind a permissive stub, the more habits and shortcuts accumulate around it that a later real Protection Layer will then have to break.
- Deferring `EOS.Dashboard` entirely to the very end, losing the chance to observe the system's real behavior while steps 1–9 are still being built and debugged.

### Rollback Strategy

Not applicable — this phase produces no artifacts to roll back. If a chosen ordering proves wrong once implementation begins (e.g., Resource Management's real thresholds require a change Reasoning Engine's already-built logic did not anticipate), that is a normal implementation-sequencing correction, not an architecture rollback — the frozen specifications themselves are unaffected either way.
## Phase 7 — Validation

### Goal

A single, repeatable set of checklists to confirm the environment is healthy at any point — after initial bootstrap, after a subsystem is added (Phase 6), or after a suspected regression.

### Tasks

Run each checklist below as a discrete, repeatable procedure (a short shell script per checklist is reasonable to write once these stabilize — but per this roadmap's own scope, no code is generated here, only the procedure).

### Ubuntu

- [ ] `uptime` shows expected load average for current activity
- [ ] `sudo apt update && apt list --upgradable` reviewed at least weekly
- [ ] `dmesg | grep -i error` shows no unexpected hardware errors
- [ ] Disk usage (`df -h /`) has at least 20% free — NVMe performance and reliability both degrade near-full

### Docker

- [ ] `docker ps` shows exactly the expected set of containers, all healthy
- [ ] `docker system df` reviewed periodically — orphaned images/volumes reclaimed with `docker system prune` (careful: confirm no needed volume is pruned)
- [ ] `docker compose config` validates the Compose file with no warnings

### AI Runtime (Ollama)

- [ ] `systemctl status ollama` shows active/running
- [ ] `ollama list` matches the expected model set
- [ ] A test generation completes within the expected latency envelope recorded in Phase 3/5

### Qwen

- [ ] The specific pulled model version is recorded (model tags can change upstream) — `ollama list` output archived alongside configuration for reproducibility
- [ ] A fixed, versioned prompt produces a stable, sane response (a basic regression smoke test, not a quality benchmark)

### ChromaDB

- [ ] Heartbeat endpoint responds
- [ ] A test collection can be created, written to, queried, and deleted without error
- [ ] Data directory size tracked over time (embedding growth is otherwise invisible until it becomes a disk problem)

### Backend (EOS.Runner and subsystem projects)

- [ ] `dotnet build` succeeds with zero warnings treated as acceptable (or a documented, reviewed list of accepted warnings — not a silently growing pile)
- [ ] `EOS.Runner` reaches "Ready" (Constitution Part 12) on a clean start
- [ ] Each implemented subsystem's own unit tests (where they exist per Phase 6) pass

### API (interfaces exposed by implemented subsystems)

- [ ] Each implemented public interface (`IKnowledgeClient`, `IReasoningEngineClient`, etc., per Phase 6's progress) responds to a basic smoke-test call
- [ ] `IProtectionClient.validate()` is confirmed to actually execute on every risk-bearing call path implemented so far (not just present in code)

### Logging

- [ ] Log files under `~/eos/data/logs` are being written and rotated (not growing unbounded)
- [ ] A deliberately-triggered error produces a corresponding, readable log entry
- [ ] Correlation IDs (once implemented, per Constitution Part 5 §5.3) are traceable across at least one multi-subsystem request

### Performance

- [ ] Recorded latency figures (Phase 3 raw inference, Phase 5 end-to-end) are still within the same order of magnitude — a regression here is an early warning sign worth investigating immediately, not deferring
- [ ] No single request causes swap usage to spike (`vmstat 1` during a test request)

### Resource Usage

- [ ] `free -h` shows sustained RAM headroom above whatever Emergency threshold Resource Management's real implementation (Phase 6, item 1) defines, once it exists — until then, above a conservative manual estimate (e.g., 4GB free at all times)
- [ ] CPU load during a representative request does not sustain at 100% across all cores for longer than the expected inference duration
- [ ] Disk I/O (`iostat`) is not persistently saturated during normal operation

### Expected Deliverables

A standing set of checklists, run after every phase and every subsystem addition, with results recorded (even informally) so regressions are visible as trends, not surprises.

### Estimated Duration

30–60 minutes per full pass; individual checklists (e.g., just "AI Runtime") take under 5 minutes and are reasonable to run after any suspicious change.

### Common Mistakes

- Only running validation once, at the very end of a phase, instead of incrementally — regressions become much harder to attribute to a specific change the longer they go unnoticed.
- Treating a passing checklist as permanent — hardware degrades, disk fills, models get replaced; re-run these checklists periodically, not just once.
- Ignoring a resource-usage checklist item because "it's probably fine" — this is precisely the category of assumption the Architecture Validation Report flagged as unvalidated; treat every resource measurement as evidence, not a formality.

### Rollback Strategy

Not applicable in the usual sense — this phase is itself the safety net for every other phase. If a checklist fails, the correct response is to identify which prior phase's change caused the regression and apply that phase's own Rollback Strategy, not to invent a new fix in place.
## Phase 8 — Backup & Recovery

### Goal

A simple, offline-first backup strategy that makes every prior phase's Rollback Strategy actually work in practice, not just in principle — and the practical bootstrap of the Weekly Restore Drill the Constitution's own Disaster Recovery Testing section already specifies, not a new invention.

### Tasks

1. **Source code.** Already covered by Git (Phase 2/4) — the only addition here is a periodic `git push` to a remote (GitHub, via the `gh` CLI already configured) or, if truly offline-only, a periodic bundle export (`git bundle create`) to an external drive.
2. **ChromaDB data.** Because Phase 3 placed ChromaDB's data under `~/eos/data/chroma` as a bind mount, a straightforward `tar czf` of that directory (with the container briefly stopped, or accepting eventual-consistency risk if taken live) is sufficient.
3. **Configuration.** Everything under Constitution Part 10's `.json` files, plus `.env` (kept out of git deliberately, per Phase 4 — meaning it must be backed up by *this* mechanism, since it is backed up by no other), included in the same archive.
4. **Prompts.** The `prompts/` directory (Constitution's Prompt Registry, Part 9) is git-tracked like any other source directory — covered by item 1, called out separately here only because the task explicitly asks for it.
5. **Logs.** Included in the backup archive for historical/debugging value, with a shorter retention policy than the categories above.

### A Simple, Concrete Backup Procedure

A single daily cron job that: stops write-heavy containers briefly (or accepts live-backup risk, a reasonable trade-off at this scale), archives `~/eos/data/{chroma,sql,redis}` plus the configuration files plus `.env` into one timestamped `tar.gz`, writes it to a second local location (an external USB drive or a second internal partition) — deliberately **not** a cloud destination by default, consistent with offline-first — and applies a simple retention policy: keep the last 7 daily archives and the last 4 weekly archives, deleting anything older.

### Expected Deliverables

A working daily backup producing a restorable archive, with at least one **actually tested** restore before this phase is considered complete.

### Estimated Duration

Half a day to set up; 5 minutes of ongoing attention per week to spot-check that backups are still running.

### Validation Checklist

- [ ] A backup archive is produced and lands in the expected location on the expected cadence
- [ ] **A full restore has actually been performed once**, into a separate directory, and the restored SQL Server/ChromaDB/Redis data verified queryable
- [ ] Retention policy correctly prunes old archives after the retention window has actually elapsed once
- [ ] `.env` is confirmed present in the backup archive despite being absent from git

### Common Mistakes

- Backing up without ever testing a restore — the single most common real-world backup failure mode.
- Backing up to a location on the same physical disk as the data, providing no protection against disk failure.
- Forgetting `.env`, since it is deliberately excluded from every other mechanism (git) in this roadmap.
- Over-engineering this into a database-specific export/import tool chain before a simple filesystem-level archive has been shown to be insufficient.

### Rollback Strategy

This phase *is* the rollback strategy for every other phase's data. Its own rollback, in the rare case the backup mechanism itself is wrong, is simply to fix the cron job/script — no data is at risk from the backup mechanism itself since it is read-only against the live data.

---

## Phase 9 — Future Growth

### Goal

Confirm, without redesigning anything, that the frozen architecture's own extensibility claims hold up as concrete, describable upgrade paths from this specific laptop-based bootstrap.

### Better Hardware

Moving to a machine with more RAM/a faster CPU requires no architectural change: restore Phase 8's backup archive onto the new machine, re-run Phases 1–4 there, and resume. Because every data store is containerized or file-based under one directory tree, migration is a copy operation, not a re-architecture.

### GPU Support

Resource-Management-Specification-v1.0's own resource-type-agnostic Allocation Manager design (that document's ADR-RM003) was built specifically so this would not require redesign. In practice: Ollama supports GPU acceleration natively (CUDA on NVIDIA hardware, ROCm on AMD) — enabling it is a matter of installing the appropriate driver/toolkit and passing the GPU through to Ollama (and to Docker, if any containerized workload needs it), plus adding a `GPU` resource-type entry to Resource Management's registry once that subsystem is real (Phase 6, item 1). No subsystem's ownership or interface changes.

### Additional Local Models

A configuration-only change: `ollama pull <new-model>` plus a new entry in AI Provider Layer's Model Registry (Phase 6, item 2) declaring its capabilities. The two-exclusive-channel design (`IAIProviderClient`/`IEmbeddingProviderClient`) already accommodates multiple registered models without any interface change.

### Multiple Repositories

**This is the one area where the Architecture Validation Report explicitly found a gap, not a designed-and-ready extension point** — no specification in this lineage addresses multi-repository support, only multi-project scoping via `domain_tags` within what the report's own review assumed was a single repository. This roadmap does not solve that gap; it flags it as a real open question that should be resolved in whatever future Configuration Schema effort closes the Validation Report's Blocker #2, before attempting a multi-repository setup in practice.

### Expected Deliverables

None — this phase is descriptive, confirming extensibility claims rather than exercising them.

### Estimated Duration

Not applicable.

### Validation Checklist

- [ ] Before pursuing any of the above, confirm the relevant subsystem (Resource Management for GPU/hardware moves, AI Provider Layer for additional models) has actually reached its Phase 6 "full" implementation state.

### Common Mistakes

- Attempting a multi-repository setup by improvising a convention, rather than waiting for the Configuration Schema work this roadmap's Phase 4 already flagged as a prerequisite.

### Rollback Strategy

Not applicable — this phase makes no changes of its own.

---

## Summary Timeline

| Phase | Estimated Duration |
|---|---|
| 1 — Prepare the Laptop | 1–2 hours |
| 2 — Development Environment | 2–3 hours |
| 3 — Core Infrastructure | 3–5 hours |
| 4 — EOS Project Bootstrap | 1–2 days |
| 5 — First Vertical Slice | 3–5 days |
| 6 — Progressive Implementation | Multi-week, per subsystem (guidance only) |
| 7 — Validation | 30–60 minutes per pass, ongoing |
| 8 — Backup & Recovery | Half a day setup, ongoing |
| 9 — Future Growth | Not applicable (descriptive) |

**From an empty laptop to a working first vertical slice: approximately one to two weeks of part-time effort**, before Phase 6's substantially larger subsystem-by-subsystem implementation work begins.
