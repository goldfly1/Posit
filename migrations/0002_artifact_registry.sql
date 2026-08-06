-- migration: 0002
-- schema: posit_artifacts
-- description: artifacts table + lineage table
-- requires: 0001

CREATE TABLE IF NOT EXISTS posit_artifacts.artifacts (
    id             TEXT NOT NULL PRIMARY KEY,
    session_id     TEXT NOT NULL,
    source_phase   TEXT NOT NULL,
    schema_version TEXT NOT NULL,
    kind           TEXT NOT NULL,
    payload_json   JSONB NOT NULL,
    produced_at    TIMESTAMPTZ NOT NULL,
    sealed_at      TIMESTAMPTZ,
    checksum       TEXT
);

CREATE INDEX IF NOT EXISTS idx_artifacts_session_phase
    ON posit_artifacts.artifacts (session_id, source_phase);

CREATE TABLE IF NOT EXISTS posit_artifacts.artifact_lineage (
    artifact_id        TEXT NOT NULL,
    parent_artifact_id TEXT NOT NULL,
    PRIMARY KEY (artifact_id, parent_artifact_id)
);