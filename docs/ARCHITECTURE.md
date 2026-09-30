# Architecture

Task Scheduler launches `SignalAtlas.Runner.exe`, which holds a named mutex, assigns its worker to a Windows Job Object, watches its heartbeat, and enforces the configured schedule limit (45 minutes by default). The Worker waits for job assignment before starting collection, persists phase transitions to SQLite, and completes collection before model loading. `SignalAtlas.App.exe` is the WPF control panel. `SignalAtlas.Diagnostics.exe` checks dependencies and data integrity.

SQLite uses WAL, foreign keys, and transactional schema migrations. Normalized text lives in SHA-256-named files, while metadata, queue state, and analysis records live in SQLite. Reports are self-contained HTML with encoded source text.

The Worker selects one `IModelBackend` from the `ai_provider` setting. LM Studio is the default and runs locally. OpenAI API uses the Responses API with a runtime `OPENAI_API_KEY`; Codex CLI uses the Windows user's ChatGPT login through ephemeral, read-only `codex exec` runs. Neither credential is written to SQLite. Cloud requests contain bounded source text and model results flow back into the same analysis, synthesis, and report pipeline.

Resource checks use Windows physical memory, power state, disk space, system CPU times, and DXGI dedicated GPU budget for the local model. Sustained CPU pressure waits up to 60 seconds before local AI loading and then uses minimum context. If a selected provider is unavailable, the Worker preserves discoveries and publishes a partial report.

The source policy is in `SignalAtlas.Core.SourcePolicy`; the opt-out setting is `block_linkedin_dokobot_reads` in SQLite. This controls only Signal Atlas's use of DokoBot, not DokoBot itself.
