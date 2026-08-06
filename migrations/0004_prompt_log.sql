-- migration: 0004
-- schema: posit_qa
-- description: Prompt→response logging for all phases. The data harvest.
--              Every model call is captured for training data and analysis.
-- requires: 0001

CREATE TABLE IF NOT EXISTS posit_qa.prompts_log (
    id              BIGSERIAL PRIMARY KEY,
    session_id      TEXT NOT NULL,
    phase_id        TEXT NOT NULL,
    phase_attempt   INT NOT NULL,
    module_name     TEXT,
    attempt_kind    TEXT NOT NULL DEFAULT 'generate',
    model_provider  TEXT,
    model_id        TEXT,
    system_prompt   TEXT,
    user_prompt     TEXT,
    response_text   TEXT,
    input_tokens    INT,
    output_tokens   INT,
    cost_usd        NUMERIC(12,6),
    latency_ms      BIGINT,
    parse_status    TEXT,
    parse_error     TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_prompts_log_session
    ON posit_qa.prompts_log(session_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_prompts_log_phase
    ON posit_qa.prompts_log(session_id, phase_id, phase_attempt);

CREATE INDEX IF NOT EXISTS idx_prompts_log_module
    ON posit_qa.prompts_log(session_id, module_name, phase_attempt);