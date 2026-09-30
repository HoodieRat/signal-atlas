CREATE TABLE IF NOT EXISTS schema_info (
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
