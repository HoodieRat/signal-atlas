# Signal Atlas
## Production Project Specification v1.0

**Target platform:** Windows 10/11 x64  
**Primary hardware target:** Windows laptop with 8 GB dedicated GPU VRAM  
**Primary use:** Scheduled topic monitoring, social/web discovery, research aggregation, local-AI analysis, and standalone HTML reports  
**Primary local inference backend:** LM Studio / llmster  
**Primary browser collection backend:** DokoBot local mode  
**Primary database:** SQLite  
**Application stack:** C# / .NET 8 / WPF  
**Operating principle:** collect first, analyze second, never overload the laptop

---

# 1. Product goal

Signal Atlas is a Windows application that automatically monitors configured topics across permitted web and social sources, collects relevant material, removes duplicates, analyzes new material with a locally hosted AI model, and produces a clean standalone summary page.

the user should not need to:

- open LM Studio
- manually load a model
- open a terminal
- manually start DokoBot
- edit JSON
- remember commands
- manually unload models
- manage VRAM
- restart hung workers
- understand model-server configuration

The normal workflow should be:

1. Open Signal Atlas.
2. Choose topics.
3. Choose sources.
4. Choose a schedule.
5. Choose an AI model.
6. Click **Enable Monitoring**.
7. Read reports.

Everything else is automated.

---

# 2. Core design principle

The v1 system **must not give the LLM autonomous control over the browser**.

The architecture is:

```text
Windows Task Scheduler
        |
        v
SignalAtlas.Runner
        |
        v
Resource / policy preflight
        |
        v
Deterministic collectors
        |
        +---- DokoBot
        +---- RSS
        +---- permitted direct websites
        +---- search discovery
        +---- approved APIs
        |
        v
Normalize + deduplicate
        |
        v
Close/release browser work
        |
        v
Resource governor
        |
        v
LM Studio / llmster
        |
        v
Local 4B model
        |
        v
Structured analysis
        |
        v
SQLite
        |
        v
Standalone HTML report
```

The AI does **not** decide what browser buttons to press or where to navigate.

The application decides what URLs and queries are permitted, retrieves their contents, and gives the resulting text to the model.

This substantially reduces:

- hallucinated browser actions
- infinite agent loops
- token consumption
- GPU/browser contention
- accidental navigation
- unpredictable runtimes
- source-policy violations
- lockups

DokoBot currently supports Windows with Chrome, Edge and Brave, and its local mode communicates directly between the CLI, native bridge and browser extension without sending page content through DokoBot's server.

---

# 3. LinkedIn policy

LinkedIn must be treated specially.

LinkedIn states that third-party bots, browser plug-ins, crawlers and extensions may not scrape or automate activity on LinkedIn.

Therefore the production application must **not perform unattended DokoBot reads of LinkedIn pages**.

The LinkedIn adapter has these permitted modes:

### Search Discovery

Automated search engines may discover publicly indexed LinkedIn URLs and snippets associated with tracked keywords.

Example concept:

```text
site:linkedin.com/posts "Unreal Engine" procedural animation
site:linkedin.com/posts "virtual production" VFX
site:linkedin.com "indie game development" Montana
```

The system may store:

- indexed URL
- indexed title
- indexed search snippet
- discovered date
- associated topic

It does not subsequently automate LinkedIn to retrieve the post.

### Manual Import

the user can paste text or URLs manually into Signal Atlas for local analysis.

### Approved API

If the user later receives appropriate official API access, an official LinkedIn API source adapter may be enabled.

### Hard policy rule

The source-policy engine must contain a built-in LinkedIn rule:

```text
linkedin.com
automated_direct_fetch = false
browser_automation = false
search_discovery = true
manual_import = true
official_api = conditional
```

This rule is not overrideable through the normal UI.

The source framework must nevertheless remain generic so Reddit, RSS feeds, blogs, forums, news sites, GitHub, company websites and future official social APIs can be added independently.

---

# 4. Technology stack

## Application

Use:

- C#
- .NET 8
- WPF
- MVVM
- `Microsoft.Data.Sqlite`
- `System.Text.Json`
- `HttpClient`
- Windows Task Scheduler integration
- Windows DPAPI or Credential Manager for secrets

Do not use Electron.

Do not require Python.

Do not require WSL.

Do not require Docker.

Do not require a continuously running development environment.

The application should publish as a **self-contained `win-x64` build** so the user does not need to install the .NET runtime separately.

Trimming should initially be disabled.

---

# 5. Process architecture

The solution contains four runtime executables.

### `SignalAtlas.exe`

Interactive WPF control panel.

Responsibilities:

- configuration
- topic management
- source management
- schedules
- model selection
- diagnostics
- report browser
- status
- manual Run Now
- pause/resume monitoring

It never performs a long research run on the UI thread.

### `SignalAtlas.Runner.exe`

Small watchdog/supervisor launched by Windows Task Scheduler.

Responsibilities:

- acquire run mutex
- verify an existing worker is not active
- launch Worker
- monitor heartbeat
- enforce maximum runtime
- recover from hangs
- request cleanup
- terminate an unrecoverable worker process tree

### `SignalAtlas.Worker.exe`

Headless research engine.

Responsibilities:

- preflight
- collection
- normalization
- deduplication
- AI analysis
- report generation
- database writes
- resource monitoring
- graceful cancellation

### `SignalAtlas.Diagnostics.exe`

Small command-line diagnostic executable used by:

- Setup
- Repair
- support
- UI Diagnostics screen

It tests dependencies without performing a full monitoring run.

---

# 6. Repository structure

```text
SignalAtlas/
│
├─ SignalAtlas.sln
│
├─ README.md
├─ LICENSE
├─ Directory.Build.props
├─ dependencies.lock.json
│
├─ src/
│  ├─ SignalAtlas.App/
│  │  ├─ App.xaml
│  │  ├─ Views/
│  │  ├─ ViewModels/
│  │  ├─ Controls/
│  │  ├─ Services/
│  │  └─ Assets/
│  │
│  ├─ SignalAtlas.Runner/
│  │  ├─ Program.cs
│  │  ├─ WorkerSupervisor.cs
│  │  ├─ HeartbeatMonitor.cs
│  │  └─ ProcessTree.cs
│  │
│  ├─ SignalAtlas.Worker/
│  │  ├─ Program.cs
│  │  ├─ RunCoordinator.cs
│  │  ├─ Pipeline/
│  │  └─ Recovery/
│  │
│  ├─ SignalAtlas.Core/
│  │  ├─ Models/
│  │  ├─ Enums/
│  │  ├─ Interfaces/
│  │  ├─ StateMachine/
│  │  ├─ Policies/
│  │  └─ Validation/
│  │
│  ├─ SignalAtlas.Data/
│  │  ├─ Database/
│  │  ├─ Repositories/
│  │  ├─ Migrations/
│  │  └─ FileStore/
│  │
│  ├─ SignalAtlas.Collectors/
│  │  ├─ DokoBot/
│  │  ├─ Search/
│  │  ├─ Rss/
│  │  ├─ DirectWeb/
│  │  ├─ ManualImport/
│  │  ├─ OfficialApis/
│  │  └─ Policy/
│  │
│  ├─ SignalAtlas.LmStudio/
│  │  ├─ LmsCliClient.cs
│  │  ├─ LmStudioHttpClient.cs
│  │  ├─ ModelManager.cs
│  │  ├─ ModelSelector.cs
│  │  ├─ StructuredInference.cs
│  │  └─ PromptFactory.cs
│  │
│  ├─ SignalAtlas.Resources/
│  │  ├─ ResourceGovernor.cs
│  │  ├─ MemoryProbe.cs
│  │  ├─ DxgiGpuProbe.cs
│  │  ├─ CpuProbe.cs
│  │  ├─ DiskProbe.cs
│  │  └─ PowerProbe.cs
│  │
│  ├─ SignalAtlas.Reporting/
│  │  ├─ ReportBuilder.cs
│  │  ├─ HtmlRenderer.cs
│  │  ├─ Templates/
│  │  └─ Sanitization/
│  │
│  └─ SignalAtlas.Diagnostics/
│
├─ tests/
│  ├─ SignalAtlas.UnitTests/
│  ├─ SignalAtlas.IntegrationTests/
│  ├─ SignalAtlas.ChaosTests/
│  └─ SignalAtlas.ResourceTests/
│
├─ scripts/
│  ├─ Setup.bat
│  ├─ Setup.ps1
│  ├─ Repair.bat
│  ├─ Repair.ps1
│  ├─ Uninstall.bat
│  ├─ Uninstall.ps1
│  ├─ Build-Release.ps1
│  └─ Diagnose.bat
│
├─ packaging/
│  ├─ manifest/
│  └─ release/
│
└─ docs/
   ├─ ARCHITECTURE.md
   ├─ SOURCE_POLICY.md
   ├─ RESOURCE_GOVERNOR.md
   ├─ TROUBLESHOOTING.md
   └─ TEST_PLAN.md
```

---

# 7. Installed folder structure

Application binaries:

```text
%LOCALAPPDATA%\Programs\SignalAtlas\
```

User data:

```text
%LOCALAPPDATA%\SignalAtlas\
│
├─ data\
│  └─ research.db
│
├─ documents\
│  └─ <content-sha256>.md
│
├─ cache\
│  ├─ searches\
│  └─ temporary\
│
├─ logs\
│  ├─ application\
│  ├─ worker\
│  └─ dependency\
│
├─ state\
│  ├─ owner.json
│  ├─ heartbeat.json
│  └─ last-run.json
│
└─ config\
   └─ settings.json
```

Human-readable reports:

```text
%USERPROFILE%\Documents\Signal Atlas\
│
├─ Latest Report.html
└─ Reports\
   ├─ 2026-09-29.html
   ├─ 2026-09-30.html
   └─ ...
```

The report must work as a standalone HTML file with no external server dependency.

---

# 8. User interface

## First-run wizard

### Screen 1: Welcome

Displays:

**Signal Atlas**

> Automatically research the topics you care about and create a private local summary.

Buttons:

- Start Setup
- Advanced Setup

### Screen 2: Browser

Detect:

- Edge
- Chrome
- Brave

Recommend Edge or Chrome.

Status indicators:

```text
Browser                Ready
DokoBot extension      Ready / Needs setup
Local bridge           Ready / Needs setup
```

If extension setup is required, open the official installation page and wait for the user to complete the one required browser permission step.

No attempt should be made to bypass Chrome extension permission dialogs.

### Screen 3: AI Model

Display installed compatible models.

Built-in recommendations:

**Fast / Recommended**

Qwen3-4B-Instruct-2507

Current LM Studio listing identifies this as a 4B non-thinking model with improvements in instruction following and tool usage, with the current listed package around 2.30 GB.

**Quality**

Qwen3.5-4B

Current LM Studio listing identifies this as a 4B model supporting reasoning and vision, with the current listed package around 3.75 GB.

Show:

```text
Model                   Qwen3-4B-Instruct-2507
Model size              2.30 GB
Context                 8,192
Estimated GPU memory    Calculating...
Safety                  SAFE
```

Buttons:

- Install Fast Model
- Install Quality Model
- Choose Existing Model
- Test Model

the user never needs to open LM Studio.

### Screen 4: Initial Topics

Examples:

```text
VFX
Motion capture
Game development
AI for game development
Procedural animation
Virtual production
Unreal Engine
```

Each topic supports:

- name
- include keywords
- exclude keywords
- synonyms
- priority
- sources
- maximum age
- maximum results per scan

### Screen 5: Schedule

Simple presets:

```text
Every morning
Morning + evening
Daily
Weekdays
Custom
```

Default:

```text
Daily at 8:00 AM
```

### Screen 6: Ready

Run:

- browser diagnostic
- DokoBot diagnostic
- database diagnostic
- resource diagnostic
- AI model diagnostic
- report diagnostic

Then display:

```text
✓ Browser connected
✓ Research database ready
✓ Qwen3-4B ready
✓ Resource protection enabled
✓ Daily schedule enabled
```

---

# 9. Main UI

The primary navigation should contain:

```text
Home
Topics
Sources
Schedule
AI Model
Reports
Diagnostics
Settings
```

## Home

Show:

```text
Signal Atlas                        RUNNING

Next scan             Tomorrow 8:00 AM
Last scan             Today 8:03 AM
Items discovered      37
New relevant items    12
Duplicates skipped    19
AI analyzed           12

System
GPU                    Safe
AI Model               Qwen3-4B
Browser                Ready

[ Run Now ] [ Pause Monitoring ]
```

Show the most recent report preview below.

## Topics

Card/table view:

```text
Topic                Enabled   Priority   New
VFX                     ✓        High      4
Game Development        ✓        High      6
Motion Capture          ✓        Normal    2
Virtual Production      ✓        Normal    3
```

Topic editor supports:

- exact phrases
- ANY keywords
- ALL keywords
- excluded terms
- synonyms
- domains
- sources
- recency
- maximum items
- active/inactive

## Sources

Display policy clearly.

Example:

```text
General Web Search        Automated
RSS                       Automated
Company Websites          Automated
Reddit                    Per configured access method
GitHub                    Automated permitted pages
LinkedIn                  Discovery only
Manual Imports            Enabled
```

LinkedIn should show:

> Signal Atlas can discover indexed LinkedIn results, but will not automatically scrape logged-in LinkedIn pages.

## Schedule

Supports multiple schedules.

Each schedule has:

- enabled
- days
- time
- topics
- run-on-start-if-missed
- wake-computer option
- report mode

## AI Model

This screen is intentionally simple.

```text
Selected Model

Qwen3-4B-Instruct-2507
Fast / Recommended

Estimated VRAM:    3.0 GB
Current free:      5.4 GB
Context:           8192
Status:            SAFE

[ Change Model ]
[ Run Benchmark ]
[ Advanced ]
```

Advanced settings:

```text
Context target        8192
Minimum context       4096
Maximum AI workers    1
Auto fallback         ON
Unload after run      ON
Resource protection   ON
```

Do not expose dozens of sampling controls in the primary UI.

## Reports

Display chronological reports.

Buttons:

- Open
- Export HTML
- Copy Summary
- Delete
- Open Reports Folder

## Diagnostics

Display each dependency:

```text
Application             Healthy
Database                Healthy
Scheduler               Healthy
DokoBot CLI             Healthy
DokoBot bridge          Healthy
Browser extension       Healthy
LM Studio daemon        Healthy
LM Studio API           Healthy
Selected model          Healthy
GPU resource probe      Healthy
```

Buttons:

- Run Full Diagnostic
- Repair Dependencies
- Open Logs

---

# 10. SQLite configuration

At connection initialization:

```sql
PRAGMA foreign_keys = ON;
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA busy_timeout = 5000;
```

All timestamps are ISO-8601 UTC strings.

Schema migrations must be versioned and transactional.

---

# 11. SQLite schema

```sql
CREATE TABLE schema_info (
    version INTEGER NOT NULL,
    applied_utc TEXT NOT NULL
);

CREATE TABLE settings (
    key TEXT PRIMARY KEY,
    value_json TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE topics (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    description TEXT,
    priority INTEGER NOT NULL DEFAULT 50,
    max_age_hours INTEGER NOT NULL DEFAULT 168,
    max_results_per_run INTEGER NOT NULL DEFAULT 30,
    enabled INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE topic_terms (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    topic_id INTEGER NOT NULL,
    term TEXT NOT NULL,
    term_type TEXT NOT NULL,
    weight REAL NOT NULL DEFAULT 1.0,
    FOREIGN KEY(topic_id) REFERENCES topics(id) ON DELETE CASCADE,
    CHECK(term_type IN ('include', 'exclude', 'synonym', 'exact'))
);

CREATE INDEX idx_topic_terms_topic
ON topic_terms(topic_id);

CREATE TABLE sources (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    source_type TEXT NOT NULL,
    base_uri TEXT,
    policy_mode TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    config_json TEXT,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    CHECK(policy_mode IN (
        'automated_direct',
        'discovery_only',
        'official_api_only',
        'manual_import_only'
    ))
);

CREATE TABLE topic_sources (
    topic_id INTEGER NOT NULL,
    source_id INTEGER NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY(topic_id, source_id),
    FOREIGN KEY(topic_id) REFERENCES topics(id) ON DELETE CASCADE,
    FOREIGN KEY(source_id) REFERENCES sources(id) ON DELETE CASCADE
);

CREATE TABLE schedules (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    enabled INTEGER NOT NULL DEFAULT 1,
    schedule_type TEXT NOT NULL,
    schedule_json TEXT NOT NULL,
    start_when_available INTEGER NOT NULL DEFAULT 1,
    wake_to_run INTEGER NOT NULL DEFAULT 0,
    max_runtime_minutes INTEGER NOT NULL DEFAULT 45,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE runs (
    id TEXT PRIMARY KEY,
    schedule_id INTEGER,
    trigger_type TEXT NOT NULL,
    status TEXT NOT NULL,
    phase TEXT NOT NULL,
    started_utc TEXT,
    finished_utc TEXT,
    heartbeat_utc TEXT,
    worker_pid INTEGER,
    discovered_count INTEGER NOT NULL DEFAULT 0,
    fetched_count INTEGER NOT NULL DEFAULT 0,
    analyzed_count INTEGER NOT NULL DEFAULT 0,
    duplicate_count INTEGER NOT NULL DEFAULT 0,
    failed_count INTEGER NOT NULL DEFAULT 0,
    deferred_reason TEXT,
    error_code TEXT,
    error_message TEXT,
    FOREIGN KEY(schedule_id) REFERENCES schedules(id)
);

CREATE INDEX idx_runs_status
ON runs(status);

CREATE INDEX idx_runs_started
ON runs(started_utc);

CREATE TABLE run_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL,
    event_utc TEXT NOT NULL,
    severity TEXT NOT NULL,
    phase TEXT,
    event_code TEXT NOT NULL,
    message TEXT NOT NULL,
    details_json TEXT,
    FOREIGN KEY(run_id) REFERENCES runs(id) ON DELETE CASCADE
);

CREATE INDEX idx_run_events_run
ON run_events(run_id, event_utc);

CREATE TABLE discoveries (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL,
    source_id INTEGER NOT NULL,
    topic_id INTEGER NOT NULL,
    discovered_url TEXT,
    canonical_url TEXT,
    title TEXT,
    snippet TEXT,
    author_name TEXT,
    published_utc TEXT,
    discovered_utc TEXT NOT NULL,
    source_external_id TEXT,
    deterministic_score REAL,
    fetch_allowed INTEGER NOT NULL DEFAULT 1,
    fetch_status TEXT NOT NULL DEFAULT 'pending',
    FOREIGN KEY(run_id) REFERENCES runs(id) ON DELETE CASCADE,
    FOREIGN KEY(source_id) REFERENCES sources(id),
    FOREIGN KEY(topic_id) REFERENCES topics(id)
);

CREATE INDEX idx_discoveries_run
ON discoveries(run_id);

CREATE INDEX idx_discoveries_canonical
ON discoveries(canonical_url);

CREATE TABLE documents (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    canonical_url TEXT,
    title TEXT,
    source_id INTEGER NOT NULL,
    author_name TEXT,
    published_utc TEXT,
    first_seen_utc TEXT NOT NULL,
    last_seen_utc TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    text_file_path TEXT,
    character_count INTEGER NOT NULL DEFAULT 0,
    language TEXT,
    metadata_json TEXT,
    FOREIGN KEY(source_id) REFERENCES sources(id)
);

CREATE UNIQUE INDEX idx_documents_hash
ON documents(content_hash);

CREATE INDEX idx_documents_url
ON documents(canonical_url);

CREATE TABLE document_topics (
    document_id INTEGER NOT NULL,
    topic_id INTEGER NOT NULL,
    deterministic_score REAL,
    llm_relevance_score REAL,
    PRIMARY KEY(document_id, topic_id),
    FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE,
    FOREIGN KEY(topic_id) REFERENCES topics(id) ON DELETE CASCADE
);

CREATE TABLE analyses (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    document_id INTEGER NOT NULL,
    model_key TEXT NOT NULL,
    prompt_version TEXT NOT NULL,
    analyzed_utc TEXT NOT NULL,
    relevance_score REAL NOT NULL,
    novelty_score REAL,
    summary TEXT NOT NULL,
    relevance_reason TEXT,
    entities_json TEXT,
    tags_json TEXT,
    claims_json TEXT,
    raw_result_json TEXT NOT NULL,
    FOREIGN KEY(document_id) REFERENCES documents(id) ON DELETE CASCADE
);

CREATE INDEX idx_analyses_document
ON analyses(document_id);

CREATE TABLE work_queue (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL,
    document_id INTEGER,
    stage TEXT NOT NULL,
    status TEXT NOT NULL,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    leased_until_utc TEXT,
    last_error TEXT,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY(run_id) REFERENCES runs(id) ON DELETE CASCADE,
    FOREIGN KEY(document_id) REFERENCES documents(id)
);

CREATE INDEX idx_work_queue_pending
ON work_queue(run_id, stage, status);

CREATE TABLE reports (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL,
    report_date TEXT NOT NULL,
    title TEXT NOT NULL,
    summary TEXT,
    html_path TEXT NOT NULL,
    generated_utc TEXT NOT NULL,
    partial INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY(run_id) REFERENCES runs(id)
);

CREATE TABLE report_items (
    report_id INTEGER NOT NULL,
    document_id INTEGER NOT NULL,
    topic_id INTEGER NOT NULL,
    display_order INTEGER NOT NULL,
    PRIMARY KEY(report_id, document_id, topic_id),
    FOREIGN KEY(report_id) REFERENCES reports(id) ON DELETE CASCADE,
    FOREIGN KEY(document_id) REFERENCES documents(id),
    FOREIGN KEY(topic_id) REFERENCES topics(id)
);

CREATE TABLE model_profiles (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    display_name TEXT NOT NULL,
    model_key TEXT NOT NULL,
    preferred_context INTEGER NOT NULL DEFAULT 8192,
    minimum_context INTEGER NOT NULL DEFAULT 4096,
    gpu_mode TEXT NOT NULL DEFAULT 'auto',
    reasoning_mode TEXT NOT NULL DEFAULT 'off',
    is_fallback INTEGER NOT NULL DEFAULT 0,
    enabled INTEGER NOT NULL DEFAULT 1,
    last_verified_utc TEXT
);

CREATE TABLE resource_samples (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    run_id TEXT NOT NULL,
    sampled_utc TEXT NOT NULL,
    phase TEXT NOT NULL,
    available_ram_bytes INTEGER,
    committed_ram_bytes INTEGER,
    gpu_budget_bytes INTEGER,
    gpu_usage_bytes INTEGER,
    cpu_percent REAL,
    battery_percent INTEGER,
    on_ac_power INTEGER,
    free_disk_bytes INTEGER,
    FOREIGN KEY(run_id) REFERENCES runs(id) ON DELETE CASCADE
);
```

Raw retrieved text is stored in the file store by SHA-256 rather than repeatedly duplicating large text blobs inside SQLite.

---

# 12. Run state machine

Run states:

```text
QUEUED
PRECHECK
COLLECTING
NORMALIZING
DEDUPLICATING
MODEL_PREP
ANALYZING
SYNTHESIZING
PUBLISHING
CLEANUP
COMPLETED
PARTIAL
DEFERRED
FAILED
CANCELLED
```

Normal transition:

```text
QUEUED
  ↓
PRECHECK
  ↓
COLLECTING
  ↓
NORMALIZING
  ↓
DEDUPLICATING
  ↓
MODEL_PREP
  ↓
ANALYZING
  ↓
SYNTHESIZING
  ↓
PUBLISHING
  ↓
CLEANUP
  ↓
COMPLETED
```

Important rule:

**COLLECTING and ANALYZING may never intentionally overlap.**

This prevents Chrome/DokoBot GPU activity from competing with the LLM.

---

# 13. Crash recovery

Every state transition is persisted before beginning the next phase.

The worker heartbeat updates every five seconds.

If a previous run exists with:

```text
status = active
heartbeat older than 120 seconds
```

it is considered abandoned.

Recovery procedure:

1. mark previous process abandoned
2. inspect persisted work queue
3. clean temporary files
4. query LM Studio for loaded models
5. unload only models owned by Signal Atlas
6. restart from the first incomplete recoverable stage

Collection items and completed analyses must never be repeated unnecessarily.

The pipeline must therefore be idempotent.

---

# 14. Scheduler

Use Windows Task Scheduler rather than an always-running polling loop.

Task name:

```text
Signal Atlas\Scheduled Research
```

Task requirements:

```text
Run only while user session is available
Start when scheduled
Run as soon as possible after missed start
Multiple instances: Ignore new instance
Maximum runtime: 45 minutes
Restart on transient failure: 2 attempts
Restart interval: 5 minutes
```

Optional:

```text
Wake computer to run
```

Default this option to OFF.

DokoBot documents that its local mode does not itself hold a wake lock, so missed scans should run when Windows next becomes available unless the user deliberately enables wake-to-run.

In addition to Task Scheduler protection, Runner obtains a named mutex:

```text
Local\SignalAtlas.Run
```

There must never be two research workers operating simultaneously.

---

# 15. Collection pipeline

Each topic generates a deterministic query plan from its stored terms.

No LLM is required to formulate scheduled searches.

Pipeline:

```text
Topic
 ↓
Stored query templates
 ↓
Source-policy validation
 ↓
Discovery
 ↓
Canonicalize URLs
 ↓
Deterministic relevance filtering
 ↓
Fetch permitted content
 ↓
Normalize text
 ↓
Hash
 ↓
Deduplicate
 ↓
Queue new documents for AI
```

DokoBot should normally operate with:

```text
local mode
single active browser operation
reuse existing tab when appropriate
explicit timeout
```

Browser concurrency:

```text
1
```

No scheduled job should open ten or twenty browser tabs simultaneously.

---

# 16. URL canonicalization

Before deduplication:

- lowercase host
- remove fragments
- normalize scheme
- normalize trailing slash
- remove known tracking parameters
- preserve content-identifying query parameters
- resolve standard redirects where permitted

Strip parameters such as:

```text
utm_source
utm_medium
utm_campaign
utm_content
utm_term
fbclid
gclid
```

Do not indiscriminately delete every query parameter.

---

# 17. Deduplication

Use three layers.

### Layer 1

Canonical URL match.

### Layer 2

Exact SHA-256 normalized-content match.

### Layer 3

Near duplicate.

Use deterministic text similarity rather than an LLM.

Suitable algorithm:

- normalize whitespace
- lowercase comparison representation
- tokenize
- SimHash or MinHash
- compare against recent documents

Threshold should be configurable.

Default:

```text
Similarity >= 0.92 = probable duplicate
```

Keep the newest source metadata while avoiding another LLM call.

---

# 18. LM Studio integration

The application uses LM Studio as an invisible inference backend.

LM Studio currently supports:

- headless operation
- `lms` command-line management
- JSON model listing
- listing loaded models
- load/unload
- custom model identifiers
- configurable context
- configurable GPU offload
- TTL automatic unload
- resource estimation before model load
- localhost REST inference APIs

Signal Atlas should use:

```text
CLI -> lifecycle management
HTTP -> inference
```

Do not parse the LM Studio GUI.

---

# 19. Model discovery

Use:

```text
lms ls --json
```

to discover installed models.

LM Studio documents JSON output for this command.

Use:

```text
lms ps --json
```

to identify loaded models.

The UI model picker displays:

- display name
- model key
- file size
- architecture
- quantization
- maximum context
- tool capability
- reasoning capability
- estimated resource requirements
- Signal Atlas compatibility

Do not hardcode model file paths.

---

# 20. Recommended model profiles

## Fast profile

```text
Name: Qwen3-4B-Instruct-2507
Context target: 8192
Context minimum: 4096
Concurrent requests: 1
Reasoning: none
Purpose: scheduled research
```

This is the default.

## Quality profile

```text
Name: Qwen3.5-4B
Context target: 8192
Context minimum: 4096
Concurrent requests: 1
Reasoning: off for scheduled summaries where supported
Purpose: richer analysis / future visual input
```

The selected model is always chosen from **inside Signal Atlas**.

---

# 21. Automatic model lifecycle

Before inference:

```text
1. Locate lms
2. Start daemon if necessary
3. Start localhost server if necessary
4. Enumerate loaded models
5. Perform memory estimate
6. Choose safe load configuration
7. Load selected model
8. Assign identifier signal-atlas
9. Verify health
10. Perform inference
11. Explicitly unload
```

Use a fixed API model identifier:

```text
signal-atlas
```

The actual model behind this identifier can therefore change without changing the inference code.

LM Studio supports custom identifiers when loading models.

---

# 22. LM Studio network security

Bind only to:

```text
127.0.0.1
```

Never:

```text
0.0.0.0
```

unless the user explicitly enables a future remote-server feature.

Do not enable CORS.

LM Studio itself warns that non-local binds and CORS broaden server exposure.

Preferred port:

```text
12345
```

Make the port configurable if unavailable.

---

# 23. VRAM/resource governor

The resource governor is mandatory.

It must never assume that "8 GB GPU" means 8 GB is available.

Windows, browser rendering, video decoding, desktop composition and other applications may already be consuming GPU memory.

## GPU measurement

Use DXGI:

```text
IDXGIAdapter3::QueryVideoMemoryInfo
```

Read:

```text
Budget
CurrentUsage
AvailableForReservation
CurrentReservation
```

This is preferred over relying only on NVIDIA-specific utilities.

Vendor-specific probes may provide additional telemetry but must not be required.

---

# 24. Memory estimation

Before loading a model, call LM Studio's estimate function using the desired context and GPU settings.

LM Studio currently supports:

```text
lms load --estimate-only <model>
```

and applies context and GPU configuration to the estimate.

The model must never be loaded before the resource governor approves it.

---

# 25. Resource safety thresholds

For an 8 GB dedicated GPU, default targets are intentionally conservative.

## Green

Run normally when:

```text
Projected GPU headroom >= 2.0 GiB
Projected GPU usage <= 75% of current DXGI budget
Available system RAM >= 6 GiB
Free disk >= 5 GiB
```

## Yellow

Reduce workload when:

```text
Projected GPU headroom 1.5-2.0 GiB
or
Available system RAM 4-6 GiB
```

Actions:

- context 8192 -> 4096
- force one inference at a time
- disable optional enrichment
- keep browser closed/released during analysis

## Red

Do not load selected model when:

```text
Projected GPU headroom < 1.5 GiB
Available system RAM < 4 GiB
Free disk < 2 GiB
```

Then attempt:

1. smaller context
2. lower GPU offload
3. configured fallback model

If none are safe:

```text
AI analysis is deferred.
```

The run still produces a report containing discovered items and links.

**Resource pressure must degrade output rather than freeze the laptop.**

---

# 26. Adaptive load algorithm

Use this sequence:

```text
Selected model
   |
   v
Estimate @ 8192 context, full GPU
   |
 SAFE? ------ yes ---> load
   |
   no
   v
Estimate @ 4096 context, full GPU
   |
 SAFE? ------ yes ---> load
   |
   no
   v
Estimate @ 4096 context, 75% GPU
   |
 SAFE? ------ yes ---> load
   |
   no
   v
Estimate fallback model
   |
 SAFE? ------ yes ---> load fallback
   |
   no
   v
Defer AI
```

Never silently reduce below the configured minimum context.

---

# 27. System-RAM governor

Use `GlobalMemoryStatusEx`.

Sample:

- physical memory available
- committed memory
- process working set
- pagefile pressure

Do not modify the Windows pagefile.

Do not change virtual-memory settings.

Do not attempt to "fix" low RAM by increasing swap during a run.

---

# 28. CPU governor

Measure rolling system CPU usage.

If average CPU exceeds 85% for 30 seconds before AI analysis:

```text
wait up to 60 seconds
```

If pressure remains:

```text
continue in reduced mode
```

The worker should run at:

```text
BelowNormal
```

process priority unless benchmarking or manually overridden.

The interactive WPF UI remains Normal priority.

---

# 29. Power policy

Read Windows power state.

Default:

```text
AC power                    Full operation
Battery >= 50%              Allowed
Battery 25-50%              Reduced mode
Battery < 25%               Defer AI
```

the user can disable battery restrictions.

Never prevent Windows from sleeping indefinitely in normal operation.

---

# 30. Runtime emergency threshold

While generating, poll resource pressure every two seconds.

Emergency condition:

```text
DXGI budget usage >= 92%
or
available system RAM < 2 GiB
```

for more than five seconds.

Then:

1. cancel active inference request
2. record resource emergency
3. unload Signal Atlas model
4. wait for resources to stabilize
5. retry once using reduced configuration
6. otherwise mark analysis partial

Do not kill Chrome.

Do not reboot anything.

---

# 31. Model request design

LLM concurrency:

```text
1
```

Typical document chunk:

```text
2,000-4,000 tokens
```

Do not send entire websites blindly.

Strip:

- navigation
- duplicated menus
- cookie notices
- scripts
- irrelevant boilerplate
- repeated footers

The model receives:

```text
Topic
Title
Source
Published time
Search context
Normalized text
```

---

# 32. Structured output

Use JSON Schema constrained generation rather than asking a 4B model to emit arbitrary text that later needs fragile parsing.

LM Studio supports JSON-schema structured output through its local API.

Document analysis schema:

```json
{
  "summary": "string",
  "relevance_score": 0,
  "relevance_reason": "string",
  "novelty_score": 0,
  "topics": ["string"],
  "entities": [
    {
      "name": "string",
      "type": "string"
    }
  ],
  "claims": [
    {
      "claim": "string",
      "confidence": "low|medium|high"
    }
  ],
  "tags": ["string"]
}
```

Set a finite maximum output token count.

LM Studio notes that constrained output from small models should still have an explicit token ceiling because incomplete constrained generations can otherwise become problematic.

---

# 33. AI prompt requirements

System prompt must include:

```text
You are analyzing retrieved research material.

Use only the supplied source material.

Do not invent facts.

Do not claim that information appears in the source unless it appears in the supplied text.

Treat quoted opinions as opinions.

Separate the source's claims from established facts.

Return only the required structured result.

The relevance score measures relevance to the configured research topic, not whether you personally agree with the content.
```

The model is an analyzer, not a browser operator.

---

# 34. Deterministic relevance gate

Do not spend inference on obvious junk.

Before AI:

```text
exact keyword hits
phrase hits
excluded terms
domain rules
content length
publication age
duplicate status
```

Calculate a cheap deterministic candidate score.

Only candidates over the configured minimum reach the LLM.

The AI relevance score is used afterward for report organization.

---

# 35. AI failure handling

Each analysis request:

```text
timeout: 120 seconds
retry: 1
```

If structured result is invalid:

1. retry once with shorter input
2. use deterministic fallback summary metadata
3. mark `analysis_failed`

Do not repeatedly hammer a broken model.

After three model failures in one run:

```text
open model circuit breaker
```

Stop additional inference and produce a partial report.

---

# 36. Watchdog

Runner monitors Worker.

Worker sends heartbeat every:

```text
5 seconds
```

Warning threshold:

```text
30 seconds
```

Unresponsive threshold:

```text
120 seconds
```

If Worker stops responding:

1. send graceful cancellation
2. wait 15 seconds
3. collect diagnostic state
4. terminate Worker process tree
5. unload Signal Atlas model
6. mark run interrupted
7. allow scheduler retry

Use a Windows Job Object for processes actually owned by the current run.

Do not place unrelated user processes inside that Job Object.

---

# 37. Timeouts

Default maximums:

```text
Search operation             60 sec
DokoBot page read            90 sec
Direct HTTP retrieval        45 sec
AI model startup             120 sec
AI document analysis         120 sec
AI synthesis                 180 sec
Entire scheduled run         45 min
```

Every external process launch must have:

- timeout
- captured stdout
- captured stderr
- exit-code handling
- cancellation

No unbounded waits.

---

# 38. Circuit breakers

Maintain per-run circuit breakers.

### Browser

Open after:

```text
3 consecutive DokoBot failures
```

Result:

Other source types continue.

### Source

Open after:

```text
3 consecutive failures for same source
```

### LM Studio

Open after:

```text
3 inference failures
```

### Database

Database write failures are fatal because reliable state cannot be guaranteed.

---

# 39. Report generation

The report must be readable without Signal Atlas running.

Output:

```text
Latest Report.html
```

and timestamped archive.

HTML must contain:

- embedded CSS
- embedded report data
- no CDN
- no remote JavaScript
- no tracking
- no analytics
- no external fonts

All external source content must be HTML-encoded before rendering.

---

# 40. Research output

Users can choose the original card digest, a separate research report, or both. The card digest retains the run scorecard, highest-relevance cards, topic groups, source context, and previously seen results. Discovery-only runs use cards.

Research report preferences include a 1–20 page target, general/technical/business profile, audience, research question, and optional specialist elements. Preferences persist and apply to manual and scheduled research.

Reports contain a subject-specific title, executive summary, developed analytical chapters, conclusions, citations, references, and scope/limitations. Longer documents include a cover and contents. Optional elements include comparison tables, timelines, risk registers, action plans, glossary, quantitative exhibits, methodology, and appendices.

The writing pipeline plans chapters, writes bounded sections from retained source evidence, and synthesizes the executive summary and conclusions. Paragraphs and table rows carry source identifiers. Chart values require matching source quotations. Missing evidence must be stated; it must not be fabricated or padded to fill pages.

Each report is produced as self-contained HTML and a paginated PDF with headers, footers, page numbers, numbered tables/figures, and linked citations. Final PDF page count is measured against the selected target. A draft exceeding the limit is shortened and rendered again; an oversized or invalid report is not published as successful. The card digest remains available if report writing fails.

Saved run records track all generated artifacts. Open/export controls expose each format separately, while deletion and retention account for related files and preserve bookmarked runs.

---

# 41. Report retention

Defaults:

```text
Reports             Keep 180 days
Normalized content  Keep 30 days
Search cache         Keep 7 days
Logs                 Keep 30 days
Temporary files      Remove after each run
```

All configurable.

Never delete starred/bookmarked report items automatically.

---

# 42. Setup.bat

`Setup.bat` exists only as the convenient entry point.

Responsibilities:

```text
1. Resolve script directory.
2. Verify PowerShell exists.
3. Launch Setup.ps1.
4. Preserve exit code.
5. Display actionable failure message if setup fails.
```

No actual installation logic belongs in the `.bat`.

This keeps quoting, registry handling and error handling in PowerShell rather than CMD.

---

# 43. Setup.ps1

Setup must be idempotent.

Running Setup twice must not break an existing installation.

Stages:

### OS check

Verify:

```text
64-bit Windows
supported Windows release
sufficient disk
current user writable install directory
```

### Application install

Copy release files to:

```text
%LOCALAPPDATA%\Programs\SignalAtlas
```

### Data initialization

Create directories.

Initialize/migrate SQLite.

### LM Studio detection

Search:

```text
PATH
%USERPROFILE%\.lmstudio\bin\
known LM Studio locations
```

If available, reuse it.

If unavailable, offer:

```text
Install LM Studio local inference runtime
```

The official LM Studio documentation currently provides a Windows headless installer and `lms daemon up` workflow.

Do not silently execute third-party installers without informing the user.

### DokoBot

Detect Node/npm.

If Node is missing, offer to install the current Windows LTS package.

Install the **tested DokoBot version specified in `dependencies.lock.json`**, not an uncontrolled version.

Then disable background CLI auto-updates:

```text
dokobot update --disable
```

DokoBot supports explicitly disabling its automatic update behavior.

This prevents an unattended update from suddenly breaking the research monitor.

Also default privacy setup to:

```text
dokobot telemetry disable
```

Install the native bridge for the user's selected browser.

Browser extension approval remains an explicit user action.

### AI model

Offer:

```text
Install Fast model
Install Quality model
Use an existing model
Skip for now
```

Model installation uses LM Studio's model-management command rather than downloading arbitrary GGUF files manually.

### Scheduler

Create Windows Task Scheduler task.

### Shortcuts

Create:

```text
Start Menu\Signal Atlas
Desktop shortcut optional
```

### Validation

Run:

```text
SignalAtlas.Diagnostics.exe --full
```

Setup succeeds only when essential checks pass.

---

# 44. Dependency locking

`dependencies.lock.json` contains:

```json
{
  "dokobot": {
    "testedVersion": "2.11.0"
  },
  "lmStudio": {
    "minimumCompatibleVersion": "project-tested-version"
  },
  "models": [
    {
      "key": "qwen/qwen3-4b-2507",
      "role": "fast"
    },
    {
      "key": "qwen/qwen3.5-4b",
      "role": "quality"
    }
  ]
}
```

Do not automatically upgrade runtime dependencies during scheduled research.

Provide an explicit:

```text
Check for Updates
```

button.

An update is:

1. downloaded
2. health-tested
3. accepted
4. activated

Scheduled jobs never upgrade dependencies.

---

# 45. Repair scripts

`Repair.bat` launches `Repair.ps1`.

Repair performs:

- verify application files
- database integrity check
- verify scheduled task
- verify DokoBot CLI
- verify native bridge
- verify browser extension connectivity
- verify `lms`
- verify LM Studio daemon
- verify model inventory
- verify localhost inference
- rebuild missing shortcuts
- recreate scheduler if missing
- clear abandoned locks
- remove stale temporary files

Repair must not delete:

- topics
- reports
- research history
- credentials
- user-selected model

unless explicitly requested.

---

# 46. Uninstall

Uninstall supports:

```text
Remove program only
Remove program + cache
Remove everything
```

Default:

```text
Remove application
Keep reports and research database
```

Uninstaller removes:

- scheduled task
- application files
- application shortcuts
- application-created temporary files

It must not uninstall:

- browser
- Node
- LM Studio
- user models
- DokoBot

unless the user explicitly selects dependency removal.

---

# 47. Security model

The application must:

- bind AI server to localhost
- sanitize report HTML
- never expose cookies
- never copy browser credential databases
- never store browser passwords
- never store raw authentication headers
- never expose API keys in logs
- redact URL authentication/query tokens
- use DPAPI/Credential Manager for secrets
- validate every source against source policy
- avoid arbitrary shell interpolation
- pass external-process arguments using structured argument lists

No collection source can execute arbitrary downloaded code.

---

# 48. Logging

Structured JSON-lines logs.

Levels:

```text
Trace
Debug
Information
Warning
Error
Critical
```

Normal production default:

```text
Information
```

Each log includes:

```text
timestamp
run_id
component
phase
event_code
duration
message
```

Do not log full retrieved article bodies by default.

---

# 49. Health monitoring

The Home screen should expose a simple status.

### Green

```text
Everything ready.
```

### Yellow

Example:

```text
AI running in reduced-memory mode.
```

### Red

Example:

```text
Browser bridge needs repair.
```

Do not expose stack traces to the user unless he opens Diagnostics.

---

# 50. Performance design

The application should optimize the expensive thing:

```text
number of LLM tokens processed
```

rather than attempting to maximize agent sophistication.

Strategies:

- deterministic filtering before AI
- deduplication before AI
- content cleaning before AI
- incremental reports
- only analyze unseen content
- 4K-8K context
- one request at a time
- compact structured output
- no agent loop

A 4B model is well suited to this workflow because the application performs orchestration and leaves the model a relatively narrow classification/summarization problem.

---

# 51. Quality strategy

The report pipeline has two AI stages.

### Document analysis

Each new document independently receives:

- summary
- topic relevance
- entities
- novelty
- important claims

### Report synthesis

After document analyses are completed, provide only their compact structured results to the LLM.

Do not send all original documents again.

This keeps final synthesis cheap.

---

# 52. Source provenance

Every source entry must preserve:

```text
source name
original URL
discovery timestamp
published timestamp when known
```

AI-generated summaries must always link back to the source from which they were generated.

No uncited AI-generated "news" should appear in the report.

---

# 53. Manual Run Now

`Run Now` performs exactly the same pipeline as scheduled operation.

It must not have a hidden alternate code path.

Optional choices:

```text
Normal scan
Discovery only
Re-analyze existing discoveries
```

---

# 54. Cancellation

When the user clicks Stop:

1. set cancellation flag
2. stop starting new collection requests
3. allow current DB transaction to complete
4. cancel current browser operation
5. cancel current inference
6. unload owned model
7. persist partial state
8. mark run CANCELLED

Application shutdown must never leave a model permanently loaded accidentally.

---

# 55. LM Studio ownership

Signal Atlas needs to know whether it started LM Studio itself.

Store:

```text
state\owner.json
```

with:

```json
{
  "serverOwnedBySignalAtlas": true,
  "modelIdentifier": "signal-atlas"
}
```

Cleanup may unload the Signal Atlas model.

It must not unload unrelated models if the user is separately using LM Studio.

---

# 56. DokoBot ownership

Signal Atlas may launch browser operations, but it must not kill the user's normal browser.

If Signal Atlas opens a dedicated tab, it may close that tab.

Never:

```text
taskkill chrome.exe
taskkill msedge.exe
```

as a cleanup mechanism.

---

# 57. Dedicated browser profile

Optional but recommended:

```text
Signal Atlas
```

browser profile.

Advantages:

- isolates research tabs
- prevents clutter
- makes extension diagnostics predictable
- reduces interference with everyday browsing

It must not be required for LinkedIn since direct automated LinkedIn reads are disabled anyway.

---

# 58. Browser concurrency

Hard limit:

```text
1 active DokoBot read
```

Future setting may permit 2.

The default must remain 1 for an 8 GB laptop.

---

# 59. Database concurrency

One Worker is the primary writer.

The WPF app may concurrently read the database.

SQLite WAL mode permits this pattern efficiently.

All write operations use short transactions.

Do not hold database transactions open while:

- waiting for HTTP
- waiting for DokoBot
- waiting for the LLM

---

# 60. Testing requirements

## Unit tests

Must cover:

- topic matching
- excludes
- query construction
- URL normalization
- URL tracking removal
- content hashing
- duplicate detection
- source-policy rules
- LinkedIn policy enforcement
- state transitions
- resource governor thresholds
- model fallback selection
- report sanitization

## Integration tests

Must test:

- SQLite migrations
- DokoBot adapter
- LM Studio lifecycle
- structured AI output
- report creation
- Task Scheduler registration

Dependencies should be mockable.

## Chaos tests

Force termination during:

```text
COLLECTING
NORMALIZING
ANALYZING
PUBLISHING
CLEANUP
```

Restart and verify:

- no database corruption
- no duplicate analysis
- no abandoned run
- no permanently loaded owned model

## Resource tests

Simulate:

```text
1 GB free VRAM
2 GB free VRAM
4 GB free VRAM
low RAM
high CPU
low battery
disk nearly full
```

System must degrade gracefully.

---

# 61. Stability acceptance test

Before v1 is accepted, it must complete:

```text
30 consecutive scheduled runs
```

without:

- deadlocked UI
- duplicate simultaneous workers
- orphaned owned model
- unrecoverable database error
- browser termination
- machine lockup caused by intentionally excessive GPU allocation

A failed source may create a partial run, but must not crash the system.

---

# 62. Resource acceptance test

On the user's target laptop:

1. run Chrome/Edge normally
2. leave normal Windows desktop workload active
3. execute Signal Atlas
4. perform collection
5. perform AI analysis
6. open generated report

Success requires:

```text
Desktop remains responsive
Browser remains responsive
No GPU allocation failure
No system out-of-memory event
No persistent model after cleanup
```

If the selected quality model cannot meet these requirements, the application must automatically use the fast model.

---

# 63. Functional acceptance test

the user must be able to perform initial setup without opening:

```text
Command Prompt
PowerShell
LM Studio GUI
SQLite editor
JSON configuration
```

After setup he can:

1. add a topic
2. select sources
3. set a schedule
4. select a model
5. run a scan
6. open the generated report

entirely from Signal Atlas.

---

# 64. Failure philosophy

The system follows this priority:

```text
1. Protect Windows responsiveness
2. Protect the user's data
3. Preserve discoveries
4. Produce a partial report
5. Use AI if resources permit
6. Maximize throughput
```

Throughput is deliberately last.

A missed AI summary is preferable to a frozen laptop.

---

# 65. Defaults for the user's 8 GB GPU laptop

Ship these defaults:

```text
AI model:
Qwen3-4B-Instruct-2507

Fallback:
same model at reduced context

Optional quality model:
Qwen3.5-4B

Preferred context:
8192

Fallback context:
4096

LLM concurrency:
1

Browser concurrency:
1

Collection and inference overlap:
Disabled

Minimum desired GPU headroom:
2.0 GiB

Hard minimum GPU headroom:
1.5 GiB

Minimum RAM before model load:
4 GiB

Preferred RAM before model load:
6 GiB

Maximum run:
45 minutes

LLM request timeout:
120 seconds

Browser read timeout:
90 seconds

Model auto-unload:
Enabled

Dependency auto-update during runs:
Disabled

Report format:
Standalone HTML

Raw content retention:
30 days

Report retention:
180 days
```

---

# 66. Features explicitly excluded from v1

Do not add these until the basic monitor is proven stable:

- autonomous multi-agent orchestration
- multiple simultaneous LLMs
- browser-driving LLM loops
- vector database
- Docker
- WSL
- cloud dependency
- public web dashboard
- automatic LinkedIn page scraping
- automated LinkedIn posting
- automated LinkedIn likes/comments/messages
- distributed workers
- GPU benchmarking suites
- complex RAG frameworks

SQLite + deterministic collection + one local model is sufficient.

---

# 67. Future extension points

Interfaces should exist for:

```text
ICollector
ISourcePolicy
IModelBackend
IResourceProbe
IReportRenderer
IScheduler
IContentNormalizer
IDeduplicator
```

This allows future additions such as:

- official social APIs
- Reddit adapters
- GitHub monitoring
- YouTube transcript monitoring
- email newsletters
- local documents
- remote LLM fallback
- alternate local inference servers

without rewriting the pipeline.

---

# 68. Build order

Implementation should proceed in this order:

1. Core models and state machine
2. SQLite schema and migrations
3. Worker pipeline
4. deterministic web collector interface
5. DokoBot adapter
6. source-policy engine
7. normalization and deduplication
8. LM Studio lifecycle manager
9. resource governor
10. structured analysis
11. HTML report generator
12. Runner/watchdog
13. WPF UI
14. Task Scheduler integration
15. Setup/Repair/Uninstall
16. integration tests
17. chaos tests
18. target-laptop validation

Do **not** start by building the polished WPF interface.

Prove the headless pipeline first.

---

# 69. Minimum viable vertical slice

The first end-to-end milestone must do only this:

```text
Topic:
"procedural animation"

↓

Perform permitted web discovery

↓

Retrieve 5 pages

↓

Normalize and deduplicate

↓

Check resources

↓

Automatically load Qwen3-4B through LM Studio

↓

Analyze results

↓

Unload model

↓

Generate Latest Report.html
```

Only after that works repeatedly should scheduling and additional sources be added.

---

# 70. Definition of done

Signal Atlas v1 is complete when the user can install it on his Windows laptop, select the local model from inside the application, define his research interests, enable a schedule, and thereafter receive useful local HTML reports without interacting with LM Studio, DokoBot commands, terminals or configuration files.

The program must manage its local model automatically, protect GPU and system resources, recover from crashes, avoid overlapping scheduled jobs, preserve its database, obey source-policy restrictions, and continue producing useful partial results when either the browser or AI backend is temporarily unavailable.

The intended user experience is:

```text
Set it up once.
Leave it alone.
Open the report.
```
