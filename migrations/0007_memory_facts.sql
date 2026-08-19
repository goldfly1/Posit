-- migration: 0007
-- schema: memory
-- description: LLM-driven memory store with pgvector retrieval.
--              Stores structured facts extracted from conversations, with
--              entity resolution, trust scoring, and 768-dim embeddings
--              (nomic-embed-text via Ollama, matching wiki.wiki_chunks).
-- requires: 0001

CREATE SCHEMA IF NOT EXISTS memory;

-- Core fact storage
CREATE TABLE IF NOT EXISTS memory.facts (
    fact_id          BIGSERIAL PRIMARY KEY,
    content          TEXT NOT NULL UNIQUE,
    category         TEXT NOT NULL DEFAULT 'general',
    tags             TEXT DEFAULT '',
    trust_score      REAL NOT NULL DEFAULT 0.5,
    retrieval_count  INTEGER NOT NULL DEFAULT 0,
    helpful_count    INTEGER NOT NULL DEFAULT 0,
    unhelpful_count  INTEGER NOT NULL DEFAULT 0,
    session_id       TEXT,
    source           TEXT DEFAULT 'conversation',
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    embedding        vector(768)
);

-- Entity resolution
CREATE TABLE IF NOT EXISTS memory.entities (
    entity_id        BIGSERIAL PRIMARY KEY,
    name             TEXT NOT NULL,
    entity_type      TEXT DEFAULT 'unknown',
    aliases          TEXT DEFAULT '',
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Fact ↔ Entity links
CREATE TABLE IF NOT EXISTS memory.fact_entities (
    fact_id          BIGINT NOT NULL REFERENCES memory.facts(fact_id) ON DELETE CASCADE,
    entity_id        BIGINT NOT NULL REFERENCES memory.entities(entity_id) ON DELETE CASCADE,
    PRIMARY KEY (fact_id, entity_id)
);

-- Indexes
CREATE INDEX IF NOT EXISTS idx_facts_trust
    ON memory.facts(trust_score DESC);
CREATE INDEX IF NOT EXISTS idx_facts_category
    ON memory.facts(category);
CREATE INDEX IF NOT EXISTS idx_facts_created
    ON memory.facts(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_facts_session
    ON memory.facts(session_id);
CREATE INDEX IF NOT EXISTS idx_facts_embedding
    ON memory.facts USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);
CREATE INDEX IF NOT EXISTS idx_entities_name
    ON memory.entities(name);
CREATE INDEX IF NOT EXISTS idx_fact_entities_entity
    ON memory.fact_entities(entity_id);

-- Auto-update updated_at on row update
CREATE OR REPLACE FUNCTION memory.touch_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_facts_touch ON memory.facts;
CREATE TRIGGER trg_facts_touch
    BEFORE UPDATE ON memory.facts
    FOR EACH ROW EXECUTE FUNCTION memory.touch_updated_at();