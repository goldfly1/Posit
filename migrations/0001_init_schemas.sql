-- migration: 0001
-- schema: public (creates application schemas)
-- description: Create pgvector extension and Posit application schemas.
-- requires: (none)

CREATE EXTENSION IF NOT EXISTS vector;

CREATE SCHEMA IF NOT EXISTS posit_meta;
CREATE SCHEMA IF NOT EXISTS posit_state;
CREATE SCHEMA IF NOT EXISTS posit_artifacts;
CREATE SCHEMA IF NOT EXISTS posit_audit;
CREATE SCHEMA IF NOT EXISTS posit_qa;

-- Migration tracking table
CREATE TABLE IF NOT EXISTS posit_meta.migrations (
    id          TEXT NOT NULL PRIMARY KEY,
    applied_at  TIMESTAMPTZ NOT NULL DEFAULT now(),
    checksum    TEXT NOT NULL,
    applied_by  TEXT NOT NULL
);