--
-- PostgreSQL database dump
--

\restrict 64H8apnQxk8lt1SZiinhcPIQc60xk7QollkrNuU53Pwzue1Cpbt9Ycl61mborzE

-- Dumped from database version 18.4 (Debian 18.4-1.pgdg12+1)
-- Dumped by pg_dump version 18.4 (Debian 18.4-1.pgdg12+1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: posit_artifacts; Type: SCHEMA; Schema: -; Owner: shepherd
--

CREATE SCHEMA posit_artifacts;


ALTER SCHEMA posit_artifacts OWNER TO shepherd;

--
-- Name: posit_audit; Type: SCHEMA; Schema: -; Owner: shepherd
--

CREATE SCHEMA posit_audit;


ALTER SCHEMA posit_audit OWNER TO shepherd;

--
-- Name: posit_meta; Type: SCHEMA; Schema: -; Owner: shepherd
--

CREATE SCHEMA posit_meta;


ALTER SCHEMA posit_meta OWNER TO shepherd;

--
-- Name: posit_qa; Type: SCHEMA; Schema: -; Owner: shepherd
--

CREATE SCHEMA posit_qa;


ALTER SCHEMA posit_qa OWNER TO shepherd;

--
-- Name: posit_registry; Type: SCHEMA; Schema: -; Owner: shepherd
--

CREATE SCHEMA posit_registry;


ALTER SCHEMA posit_registry OWNER TO shepherd;

--
-- Name: posit_state; Type: SCHEMA; Schema: -; Owner: shepherd
--

CREATE SCHEMA posit_state;


ALTER SCHEMA posit_state OWNER TO shepherd;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: artifact_lineage; Type: TABLE; Schema: posit_artifacts; Owner: shepherd
--

CREATE TABLE posit_artifacts.artifact_lineage (
    artifact_id text NOT NULL,
    parent_artifact_id text NOT NULL
);


ALTER TABLE posit_artifacts.artifact_lineage OWNER TO shepherd;

--
-- Name: artifacts; Type: TABLE; Schema: posit_artifacts; Owner: shepherd
--

CREATE TABLE posit_artifacts.artifacts (
    id text NOT NULL,
    session_id text NOT NULL,
    source_phase text NOT NULL,
    schema_version text NOT NULL,
    kind text NOT NULL,
    payload_json jsonb NOT NULL,
    produced_at timestamp with time zone NOT NULL,
    sealed_at timestamp with time zone,
    checksum text
);


ALTER TABLE posit_artifacts.artifacts OWNER TO shepherd;

--
-- Name: events; Type: TABLE; Schema: posit_audit; Owner: shepherd
--

CREATE TABLE posit_audit.events (
    id bigint NOT NULL,
    session_id text NOT NULL,
    event_type text NOT NULL,
    phase_id text,
    severity text DEFAULT 'info'::text NOT NULL,
    payload jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE posit_audit.events OWNER TO shepherd;

--
-- Name: events_id_seq; Type: SEQUENCE; Schema: posit_audit; Owner: shepherd
--

CREATE SEQUENCE posit_audit.events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE posit_audit.events_id_seq OWNER TO shepherd;

--
-- Name: events_id_seq; Type: SEQUENCE OWNED BY; Schema: posit_audit; Owner: shepherd
--

ALTER SEQUENCE posit_audit.events_id_seq OWNED BY posit_audit.events.id;


--
-- Name: migrations; Type: TABLE; Schema: posit_meta; Owner: shepherd
--

CREATE TABLE posit_meta.migrations (
    id text NOT NULL,
    applied_at timestamp with time zone DEFAULT now() NOT NULL,
    checksum text NOT NULL,
    applied_by text NOT NULL
);


ALTER TABLE posit_meta.migrations OWNER TO shepherd;

--
-- Name: dafny_results; Type: TABLE; Schema: posit_qa; Owner: shepherd
--

CREATE TABLE posit_qa.dafny_results (
    id bigint NOT NULL,
    session_id text NOT NULL,
    phase_id text NOT NULL,
    module_name text NOT NULL,
    dafny_source text NOT NULL,
    is_verified boolean DEFAULT false NOT NULL,
    verification_output text,
    translated_csharp text,
    contract_summary text,
    attempt_number integer DEFAULT 1 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE posit_qa.dafny_results OWNER TO shepherd;

--
-- Name: dafny_results_id_seq; Type: SEQUENCE; Schema: posit_qa; Owner: shepherd
--

CREATE SEQUENCE posit_qa.dafny_results_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE posit_qa.dafny_results_id_seq OWNER TO shepherd;

--
-- Name: dafny_results_id_seq; Type: SEQUENCE OWNED BY; Schema: posit_qa; Owner: shepherd
--

ALTER SEQUENCE posit_qa.dafny_results_id_seq OWNED BY posit_qa.dafny_results.id;


--
-- Name: prompts_log; Type: TABLE; Schema: posit_qa; Owner: shepherd
--

CREATE TABLE posit_qa.prompts_log (
    id bigint NOT NULL,
    session_id text NOT NULL,
    phase_id text NOT NULL,
    phase_attempt integer NOT NULL,
    module_name text,
    attempt_kind text DEFAULT 'generate'::text NOT NULL,
    model_provider text,
    model_id text,
    system_prompt text,
    user_prompt text,
    response_text text,
    input_tokens integer,
    output_tokens integer,
    cost_usd numeric(12,6),
    latency_ms bigint,
    parse_status text,
    parse_error text,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE posit_qa.prompts_log OWNER TO shepherd;

--
-- Name: prompts_log_id_seq; Type: SEQUENCE; Schema: posit_qa; Owner: shepherd
--

CREATE SEQUENCE posit_qa.prompts_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE posit_qa.prompts_log_id_seq OWNER TO shepherd;

--
-- Name: prompts_log_id_seq; Type: SEQUENCE OWNED BY; Schema: posit_qa; Owner: shepherd
--

ALTER SEQUENCE posit_qa.prompts_log_id_seq OWNED BY posit_qa.prompts_log.id;


--
-- Name: variants; Type: TABLE; Schema: posit_registry; Owner: shepherd
--

CREATE TABLE posit_registry.variants (
    id text NOT NULL,
    pattern text NOT NULL,
    params jsonb DEFAULT '{}'::jsonb NOT NULL,
    description text DEFAULT ''::text NOT NULL,
    source_path text NOT NULL,
    verified boolean DEFAULT false NOT NULL,
    vc_count integer DEFAULT 0 NOT NULL,
    tokens integer DEFAULT 0 NOT NULL,
    priority integer DEFAULT 0 NOT NULL,
    embedding public.vector(768),
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE posit_registry.variants OWNER TO shepherd;

--
-- Name: session_contexts; Type: TABLE; Schema: posit_state; Owner: shepherd
--

CREATE TABLE posit_state.session_contexts (
    session_id text NOT NULL,
    context_json jsonb NOT NULL,
    saved_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE posit_state.session_contexts OWNER TO shepherd;

--
-- Name: sessions; Type: TABLE; Schema: posit_state; Owner: shepherd
--

CREATE TABLE posit_state.sessions (
    session_id text NOT NULL,
    state_json jsonb NOT NULL,
    saved_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE posit_state.sessions OWNER TO shepherd;

--
-- Name: events id; Type: DEFAULT; Schema: posit_audit; Owner: shepherd
--

ALTER TABLE ONLY posit_audit.events ALTER COLUMN id SET DEFAULT nextval('posit_audit.events_id_seq'::regclass);


--
-- Name: dafny_results id; Type: DEFAULT; Schema: posit_qa; Owner: shepherd
--

ALTER TABLE ONLY posit_qa.dafny_results ALTER COLUMN id SET DEFAULT nextval('posit_qa.dafny_results_id_seq'::regclass);


--
-- Name: prompts_log id; Type: DEFAULT; Schema: posit_qa; Owner: shepherd
--

ALTER TABLE ONLY posit_qa.prompts_log ALTER COLUMN id SET DEFAULT nextval('posit_qa.prompts_log_id_seq'::regclass);


--
-- Name: artifact_lineage artifact_lineage_pkey; Type: CONSTRAINT; Schema: posit_artifacts; Owner: shepherd
--

ALTER TABLE ONLY posit_artifacts.artifact_lineage
    ADD CONSTRAINT artifact_lineage_pkey PRIMARY KEY (artifact_id, parent_artifact_id);


--
-- Name: artifacts artifacts_pkey; Type: CONSTRAINT; Schema: posit_artifacts; Owner: shepherd
--

ALTER TABLE ONLY posit_artifacts.artifacts
    ADD CONSTRAINT artifacts_pkey PRIMARY KEY (id);


--
-- Name: events events_pkey; Type: CONSTRAINT; Schema: posit_audit; Owner: shepherd
--

ALTER TABLE ONLY posit_audit.events
    ADD CONSTRAINT events_pkey PRIMARY KEY (id);


--
-- Name: migrations migrations_pkey; Type: CONSTRAINT; Schema: posit_meta; Owner: shepherd
--

ALTER TABLE ONLY posit_meta.migrations
    ADD CONSTRAINT migrations_pkey PRIMARY KEY (id);


--
-- Name: dafny_results dafny_results_pkey; Type: CONSTRAINT; Schema: posit_qa; Owner: shepherd
--

ALTER TABLE ONLY posit_qa.dafny_results
    ADD CONSTRAINT dafny_results_pkey PRIMARY KEY (id);


--
-- Name: prompts_log prompts_log_pkey; Type: CONSTRAINT; Schema: posit_qa; Owner: shepherd
--

ALTER TABLE ONLY posit_qa.prompts_log
    ADD CONSTRAINT prompts_log_pkey PRIMARY KEY (id);


--
-- Name: variants variants_pkey; Type: CONSTRAINT; Schema: posit_registry; Owner: shepherd
--

ALTER TABLE ONLY posit_registry.variants
    ADD CONSTRAINT variants_pkey PRIMARY KEY (id);


--
-- Name: session_contexts session_contexts_pkey; Type: CONSTRAINT; Schema: posit_state; Owner: shepherd
--

ALTER TABLE ONLY posit_state.session_contexts
    ADD CONSTRAINT session_contexts_pkey PRIMARY KEY (session_id);


--
-- Name: sessions sessions_pkey; Type: CONSTRAINT; Schema: posit_state; Owner: shepherd
--

ALTER TABLE ONLY posit_state.sessions
    ADD CONSTRAINT sessions_pkey PRIMARY KEY (session_id);


--
-- Name: idx_artifacts_session_phase; Type: INDEX; Schema: posit_artifacts; Owner: shepherd
--

CREATE INDEX idx_artifacts_session_phase ON posit_artifacts.artifacts USING btree (session_id, source_phase);


--
-- Name: idx_events_session; Type: INDEX; Schema: posit_audit; Owner: shepherd
--

CREATE INDEX idx_events_session ON posit_audit.events USING btree (session_id, created_at DESC);


--
-- Name: idx_events_type; Type: INDEX; Schema: posit_audit; Owner: shepherd
--

CREATE INDEX idx_events_type ON posit_audit.events USING btree (session_id, event_type);


--
-- Name: idx_dafny_results_session; Type: INDEX; Schema: posit_qa; Owner: shepherd
--

CREATE INDEX idx_dafny_results_session ON posit_qa.dafny_results USING btree (session_id, module_name, created_at DESC);


--
-- Name: idx_dafny_results_verified; Type: INDEX; Schema: posit_qa; Owner: shepherd
--

CREATE INDEX idx_dafny_results_verified ON posit_qa.dafny_results USING btree (session_id, is_verified);


--
-- Name: idx_prompts_log_module; Type: INDEX; Schema: posit_qa; Owner: shepherd
--

CREATE INDEX idx_prompts_log_module ON posit_qa.prompts_log USING btree (session_id, module_name, phase_attempt);


--
-- Name: idx_prompts_log_phase; Type: INDEX; Schema: posit_qa; Owner: shepherd
--

CREATE INDEX idx_prompts_log_phase ON posit_qa.prompts_log USING btree (session_id, phase_id, phase_attempt);


--
-- Name: idx_prompts_log_session; Type: INDEX; Schema: posit_qa; Owner: shepherd
--

CREATE INDEX idx_prompts_log_session ON posit_qa.prompts_log USING btree (session_id, created_at DESC);


--
-- Name: idx_variants_embedding; Type: INDEX; Schema: posit_registry; Owner: shepherd
--

CREATE INDEX idx_variants_embedding ON posit_registry.variants USING hnsw (embedding public.vector_cosine_ops) WITH (m='16', ef_construction='64');


--
-- Name: idx_variants_params; Type: INDEX; Schema: posit_registry; Owner: shepherd
--

CREATE INDEX idx_variants_params ON posit_registry.variants USING gin (params);


--
-- Name: idx_variants_pattern; Type: INDEX; Schema: posit_registry; Owner: shepherd
--

CREATE INDEX idx_variants_pattern ON posit_registry.variants USING btree (pattern);


--
-- Name: idx_variants_priority; Type: INDEX; Schema: posit_registry; Owner: shepherd
--

CREATE INDEX idx_variants_priority ON posit_registry.variants USING btree (priority DESC);


--
-- Name: idx_variants_verified; Type: INDEX; Schema: posit_registry; Owner: shepherd
--

CREATE INDEX idx_variants_verified ON posit_registry.variants USING btree (verified);


--
-- PostgreSQL database dump complete
--

\unrestrict 64H8apnQxk8lt1SZiinhcPIQc60xk7QollkrNuU53Pwzue1Cpbt9Ycl61mborzE

