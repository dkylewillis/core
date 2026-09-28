-- CORE review store (.corereview), schema version 1. DRAFT for owner review.
--
-- Holds rule runs, observations, findings, evidence, and dispositions across one or
-- more snapshots of the same project (ADR-009). References snapshots by id and
-- checksum, and canonical entities by stable key, never by .core row id.
--
-- Requires SQLite 3.37+ (STRICT tables).

PRAGMA foreign_keys = ON;

CREATE TABLE meta (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
) STRICT;
-- Required keys: schema_version ('1'), project_id.

CREATE TABLE snapshot_ref (
  snapshot_id   TEXT PRIMARY KEY,
  core_sha256   TEXT NOT NULL,
  corex_sha256  TEXT NOT NULL,
  core_path     TEXT,
  added_at      TEXT NOT NULL,
  sequence      INTEGER NOT NULL UNIQUE
) STRICT;
-- sequence orders snapshots of the project (submittal 1, 2, ...).

CREATE TABLE rule (
  id       TEXT NOT NULL,
  version  INTEGER NOT NULL,
  kind     TEXT NOT NULL CHECK (kind IN ('deterministic', 'ai-assisted')),
  spec_ref TEXT NOT NULL,
  PRIMARY KEY (id, version)
) STRICT;

CREATE TABLE rule_profile (
  sha256       TEXT PRIMARY KEY,
  name         TEXT NOT NULL,
  source       TEXT NOT NULL,
  content_json TEXT NOT NULL
) STRICT;

CREATE TABLE rule_run (
  id                  INTEGER PRIMARY KEY,
  rule_id             TEXT NOT NULL,
  rule_version        INTEGER NOT NULL,
  profile_sha256      TEXT NOT NULL REFERENCES rule_profile(sha256),
  snapshot_id         TEXT NOT NULL REFERENCES snapshot_ref(snapshot_id),
  params_json         TEXT NOT NULL,
  engine_version      TEXT NOT NULL,
  started_at          TEXT NOT NULL,
  finished_at         TEXT,
  status              TEXT NOT NULL CHECK (status IN ('running', 'succeeded', 'failed')),
  error               TEXT,
  FOREIGN KEY (rule_id, rule_version) REFERENCES rule(id, version)
) STRICT;

CREATE TABLE observation (
  id           INTEGER PRIMARY KEY,
  rule_run_id  INTEGER NOT NULL REFERENCES rule_run(id),
  code         TEXT NOT NULL,
  basis        TEXT NOT NULL CHECK (basis IN ('native', 'rule', 'ai')),
  subject_key  TEXT NOT NULL,
  facts_json   TEXT NOT NULL
) STRICT;

CREATE TABLE finding (
  id                     INTEGER PRIMARY KEY,
  rule_run_id            INTEGER NOT NULL REFERENCES rule_run(id),
  finding_key            TEXT NOT NULL,
  code                   TEXT NOT NULL,
  severity               TEXT NOT NULL CHECK (severity IN ('high', 'medium', 'low')),
  basis                  TEXT NOT NULL CHECK (basis IN ('native', 'rule', 'ai')),
  subject_key            TEXT NOT NULL,
  sheet_number           TEXT,
  title                  TEXT NOT NULL,
  facts_json             TEXT NOT NULL,
  status                 TEXT NOT NULL DEFAULT 'open' CHECK (status IN ('open', 'accepted', 'dismissed', 'deferred', 'fixed', 'needs-confirmation', 'resolved-by-change')),
  carried_from_id        INTEGER REFERENCES finding(id),
  carry_match            TEXT CHECK (carry_match IN ('exact', 'fallback')),
  UNIQUE (rule_run_id, finding_key),
  CHECK ((carried_from_id IS NULL) = (carry_match IS NULL)),
  CHECK (carry_match <> 'fallback' OR status IN ('needs-confirmation', 'open', 'accepted', 'dismissed', 'deferred', 'fixed'))
) STRICT;
-- finding_key = rule id + code + subject stable key (+ sheet number for sheet-scoped rules).
-- status is the latest disposition, or needs-confirmation after a fallback carry, or
-- resolved-by-change when a later snapshot no longer produces the finding.

CREATE TABLE finding_observation (
  finding_id     INTEGER NOT NULL REFERENCES finding(id),
  observation_id INTEGER NOT NULL REFERENCES observation(id),
  PRIMARY KEY (finding_id, observation_id)
) STRICT;

CREATE TABLE evidence (
  id              INTEGER PRIMARY KEY,
  finding_id      INTEGER REFERENCES finding(id),
  observation_id  INTEGER REFERENCES observation(id),
  role            TEXT NOT NULL CHECK (role IN ('subject', 'related', 'measurement', 'threshold')),
  target_kind     TEXT NOT NULL CHECK (target_kind IN ('source-entity', 'design-object', 'xref-instance', 'data-reference', 'viewport', 'layout', 'sheet', 'document-region')),
  target_key      TEXT NOT NULL,
  values_json     TEXT NOT NULL,
  CHECK ((finding_id IS NULL) <> (observation_id IS NULL))
) STRICT;

CREATE TABLE disposition (
  id          INTEGER PRIMARY KEY,
  finding_id  INTEGER NOT NULL REFERENCES finding(id),
  decision    TEXT NOT NULL CHECK (decision IN ('accepted', 'dismissed', 'deferred', 'fixed', 'confirmed-carry')),
  reason      TEXT NOT NULL,
  reviewer    TEXT NOT NULL,
  decided_at  TEXT NOT NULL,
  inherited_from_id INTEGER REFERENCES disposition(id)
) STRICT;
-- Dispositions are append-only. A disposition inherited by an exact carry records
-- inherited_from_id; a fallback carry inherits nothing until confirmed.

CREATE INDEX ix_finding_key ON finding(finding_key);
CREATE INDEX ix_finding_status ON finding(status);
CREATE INDEX ix_rule_run_snapshot ON rule_run(snapshot_id);
CREATE INDEX ix_disposition_finding ON disposition(finding_id);
