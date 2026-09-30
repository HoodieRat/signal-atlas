# Signal Atlas — implementation plan

Source of truth: [docs/PRODUCTION_SPEC.md](docs/PRODUCTION_SPEC.md), the user-provided v1.0 specification.

Later user-directed additions: the LinkedIn design in [docs/LINKEDIN_DESIGN.md](docs/LINKEDIN_DESIGN.md), and optional OpenAI API and Codex with ChatGPT providers in [docs/AI_PROVIDERS.md](docs/AI_PROVIDERS.md). These preserve the local provider as the default.

## Delivery sequence

1. Create the .NET 8 solution, core contracts, state machine, configuration, and a versioned SQLite schema.
2. Build a recoverable headless worker: deterministic queries, source policy, discovery and fetch adapters, normalization, deduplication, durable queue, and partial-result handling.
3. Add resource probes and a conservative governor before any model load. Integrate LM Studio CLI lifecycle and schema-constrained localhost inference with owned-model cleanup.
4. Render encoded, standalone HTML reports with provenance. Add a runner with a named mutex, heartbeat supervision, timeout, and cancellation.
5. Build the WPF setup and control panel for topics, sources, schedules, model choice, reports, diagnostics, and manual runs.
6. Register Task Scheduler jobs and provide idempotent setup, repair, uninstall, and self-contained publishing scripts.
7. Add meaningful unit and integration tests, then build and run local diagnostics and the available end-to-end path.

## Invariants

- LinkedIn discovery is automatic. **Block LinkedIn DokoBot reads** defaults on. Turning it off permits sequential DokoBot reads of multiple discovered LinkedIn URLs in scheduled scans and selected batches in the UI. DokoBot itself is never modified.
- A user-initiated LinkedIn capture can also import text he copied, with URL and topic provenance, into the normal document queue.
- Browser collection ends before model analysis begins; concurrency for each is one.
- Only a model loaded under the fixed `signal-atlas` identifier by this application may be unloaded.
- External content is treated as untrusted data and HTML-encoded in reports.
- Database writes are short, with WAL, foreign keys, busy timeout, and transactional migrations.
- Resource uncertainty defers AI while preserving discoveries and publishing a partial report.
- Scheduled jobs never install or upgrade dependencies.

## Verification gates

- `dotnet build` and `dotnet test` pass.
- Diagnostics can initialize and check the database without running research.
- The worker can run a deterministic local fixture and produce a standalone report.
- Live DokoBot, LM Studio, scheduler, and target-laptop acceptance require those dependencies and the target laptop; record actual results instead of assuming success.

## Implementation status (2026-09-29)

Steps 1–6 are implemented in the solution and release package. The runner assigns its worker to a Windows Job Object before collection, and each enabled schedule registers its own Windows task with chosen topics, report mode, wake behavior, missed-run behavior, and maximum runtime. The app has a six-step setup flow and report-item bookmarks; automatic retention preserves bookmarked items. The source-policy UI displays the effective LinkedIn setting while retaining the existing SQLite policy value for migration compatibility.

The available development-machine gates passed: 15 tests, self-contained release publication, full diagnostics including a local DokoBot bridge read, temporary scheduler registration, packaged WPF startup, packaged discovery-only report publication, automatic indexed LinkedIn discovery with page reads blocked, and bounded two-page LinkedIn scans and chosen batches with the block off. See [validation results](docs/TEST_PLAN.md).

Target-laptop model inference, fault-injection recovery, and 30 consecutive scheduled runs require validation in the user's environment. These are acceptance checks, not claims of completed testing.
