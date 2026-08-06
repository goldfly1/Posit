-- migration: 0006
-- schema: posit_qa
-- description: Dafny verification results + Z3 output capture.
--              Records every Z3 verification attempt (skeleton and body)
--              with the full Dafny source, verification output, and translated C#.
-- requires: 0001

CREATE TABLE IF NOT EXISTS posit_qa.dafny_results (
    id                  BIGSERIAL PRIMARY KEY,
    session_id          TEXT NOT NULL,
    phase_id            TEXT NOT NULL,
    module_name         TEXT NOT NULL,
    dafny_source        TEXT NOT NULL,
    is_verified         BOOLEAN NOT NULL DEFAULT FALSE,
    verification_output TEXT,
    translated_csharp   TEXT,
    contract_summary    TEXT,
    attempt_number      INT NOT NULL DEFAULT 1,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_dafny_results_session
    ON posit_qa.dafny_results(session_id, module_name, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_dafny_results_verified
    ON posit_qa.dafny_results(session_id, is_verified);