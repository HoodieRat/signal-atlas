# Test plan

- Run `dotnet test SignalAtlas.sln` for policy, URL, matching, hashing, resource, schema, capture, scheduler XML, and report-encoding tests.
- Verify cards, report-only, and combined output. Check report preferences after restart, source-text loading, citation validation, one-page PDF pagination, table/chart rendering, long-document contents and page references, PDF limits, and export without broken sibling links. A failed report must retain a clearly labeled card digest. Inspect rendered PDF pages for clipping and overlapping content.
- Run `python scripts/check_secrets.py` on the staged Git contents before each push. Confirm that local databases, reports, account state, and generated packages are ignored.
- Test OpenAI API request construction with a fake HTTP handler and Codex CLI sign-in/structured output with a fake process runner. Live account tests require a user-selected provider and active account access.
- Run `SignalAtlas.Diagnostics.exe --full` on the target machine.
- Build the Windows ZIP with `scripts/Package-Release.ps1`, confirm it contains `scripts/Setup.bat` and the four packaged executables, and scan its contents for credential patterns before attaching it to a release.
- From the UI, add a topic, run discovery, verify a standalone report, then test with a locally installed 4B model and confirm owned-model unload.
- Test the LinkedIn block both on and off with a small, explicitly chosen batch before enabling scheduled reads.
- Test pause/resume, Task Scheduler registration, 45-minute timeout, cancellation, abrupt Worker termination, and restart recovery.
- The v1 stability gate requires 30 consecutive scheduled runs and target-laptop responsiveness checks. This cannot be asserted from development-machine unit tests.

## Results on the development computer (2026-09-29)

### Signal Atlas public-source run

- Renamed solution: 33 tests pass. The report PDF uses compact spacing at page targets through five; the real generated demo report occupies four pages with its references on the final page. All four rendered pages were inspected for clipping and layout defects.
- With an isolated data root and the signed-in Codex provider, the Worker discovered three Epic Games pages, fetched all three, analyzed all three, and completed with zero failed items. It saved the card digest, narrative HTML, cited PDF, and report data. The actual PDF and standalone HTML copies are in `demo/`.
- The narrated demo uses crops of that PDF and the verified run counts. Its MP4 contains 1920×1080 H.264 video, AAC audio, and 33 caption cues. The Windows app capture tool returned a black frame, so UI control footage remains unverified.
- Live LM Studio and OpenAI API report generation, target-machine acceptance, and 30-run endurance remain unverified.

### Report-mode update

- Solution build passed with zero warnings and errors; report tests cover invalid citations, invented chart measurements, required specialized tables, saved preferences and artifact paths, retained source text, HTML encoding, increasing content budgets, and PDF page-limit checks.
- PDF layout fixtures rendered at 1 and 20 pages. Every page was inspected for layout, with chart-axis and chart-rendering defects corrected during QA. Long-table headers repeat across pages.
- A real Codex report-writing round trip completed from project source files, producing a five-page technical assessment with a comparison table, risk register, action plan, citations, and references. This validates report generation through that provider; it does not establish output quality for every research topic or local model.
- The actual Worker completed a separate live run in an isolated database from a local README capture. It honored saved one-page/combined-output preferences and persisted cards, narrative HTML, PDF, and report data, then cleared report progress.
- Live OpenAI API and LM Studio report generation, target-machine UI acceptance, and the endurance gate remain unverified.

### Earlier baseline from the predecessor project

- Four self-contained win-x64 executables published successfully.
- Fifteen unit and integration tests passed, including full-text chosen capture, policy gating, public-search LinkedIn fallback, report encoding, report-item bookmarking, recovery, schedule XML, and LM Studio lifecycle with a fake CLI/API.
- Temporary Windows Task Scheduler registration succeeded and was removed.
- Full diagnostics passed for SQLite, DokoBot CLI, a real local DokoBot browser-bridge read of `example.com`, LM Studio CLI, disk, RAM, CPU sampling, reports, and browser presence.
- Packaged WPF app remained running through a startup smoke check.
- Packaged Runner generated a standalone report in an isolated data root. A separate LinkedIn discovery-only run produced a report containing 27 LinkedIn URL mentions while page reads were blocked.
- The local GPU resource probe deferred live AI inference; model-backed acceptance on the user's target laptop remains unverified.
- With the LinkedIn block off in a separate test database, a bounded scheduled scan discovered two LinkedIn URLs and stored two sequential DokoBot page reads in SQLite. Their normalized text lengths were 11,753 and 145 characters. A separate user-selected two-URL capture batch stored both pages through the same worker path. Both reports linked back to LinkedIn. The shorter page did not meet the AI queue threshold; the longer page remained queued because the selected local model did not pass availability/resource checks on this computer.
- The 30-run endurance gate, abrupt-termination recovery, and model-backed analysis on the target laptop remain unverified.
