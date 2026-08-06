-- migration: 0003
-- schema: posit_state
-- description: session state persistence + design context snapshots
-- requires: 0001

CREATE TABLE IF NOT EXISTS posit_state.sessions (
    session_id    TEXT NOT NULL PRIMARY KEY,
    state_json    JSONB NOT NULL,
    saved_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS posit_state.session_contexts (
    session_id   TEXT NOT NULL PRIMARY KEY,
    context_json JSONB NOT NULL,
    saved_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);