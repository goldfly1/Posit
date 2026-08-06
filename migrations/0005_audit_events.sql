-- migration: 0005
-- schema: posit_audit
-- description: Append-only audit event log for the pipeline.
--              Every phase transition, correction signal, and model call is recorded.
-- requires: 0001

CREATE TABLE IF NOT EXISTS posit_audit.events (
    id          BIGSERIAL PRIMARY KEY,
    session_id  TEXT NOT NULL,
    event_type  TEXT NOT NULL,
    phase_id    TEXT,
    severity    TEXT NOT NULL DEFAULT 'info',
    payload     JSONB,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_events_session
    ON posit_audit.events(session_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_events_type
    ON posit_audit.events(session_id, event_type);