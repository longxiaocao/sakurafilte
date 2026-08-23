--
-- PostgreSQL database dump
--

-- Dumped from database version 17.4
-- Dumped by pg_dump version 17.4

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
-- Name: pg_trgm; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;


--
-- Name: EXTENSION pg_trgm; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION pg_trgm IS 'text similarity measurement and index searching based on trigrams';


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: __EFMigrationsHistory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public."__EFMigrationsHistory" (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL
);


--
-- Name: alert_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.alert_history (
    id bigint NOT NULL,
    type character varying(64) NOT NULL,
    severity character varying(16) NOT NULL,
    title character varying(256) NOT NULL,
    content_json jsonb NOT NULL,
    channel character varying(32) NOT NULL,
    status character varying(16) NOT NULL,
    response text,
    error text,
    recipients jsonb,
    correlation_id uuid,
    sent_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: alert_history_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.alert_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: alert_history_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.alert_history_id_seq OWNED BY public.alert_history.id;


--
-- Name: alert_rules; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.alert_rules (
    id bigint NOT NULL,
    type character varying(64) NOT NULL,
    enabled boolean DEFAULT true NOT NULL,
    severity character varying(16) NOT NULL,
    channels jsonb NOT NULL,
    conditions jsonb,
    recipients jsonb,
    description text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: alert_rules_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.alert_rules_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: alert_rules_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.alert_rules_id_seq OWNED BY public.alert_rules.id;


--
-- Name: auth_token_state; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.auth_token_state (
    id smallint NOT NULL,
    current_key character varying(128) NOT NULL,
    previous_key character varying(128),
    rotated_at timestamp with time zone,
    rotated_by character varying(64)
);


--
-- Name: cross_references; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cross_references (
    id bigint NOT NULL,
    product_id bigint NOT NULL,
    product_name_1 character varying(100),
    oem_brand character varying(100) DEFAULT ''::character varying NOT NULL,
    oem_no_3 character varying(200) DEFAULT ''::character varying NOT NULL,
    is_discontinued boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now(),
    sort_order integer DEFAULT 0 NOT NULL,
    machine_type character varying(50) DEFAULT 'others'::character varying,
    is_published boolean DEFAULT true NOT NULL,
    oem_2 character varying(100),
    CONSTRAINT chk_xref_machine_type CHECK (((machine_type IS NULL) OR ((machine_type)::text = ANY ((ARRAY['agriculture'::character varying, 'commercial'::character varying, 'construction'::character varying, 'industrial'::character varying, 'others'::character varying])::text[]))))
);


--
-- Name: cross_references_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cross_references_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cross_references_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cross_references_id_seq OWNED BY public.cross_references.id;


--
-- Name: dict_engine; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dict_engine (
    id bigint NOT NULL,
    engine_brand character varying(200) NOT NULL,
    engine_type character varying(200),
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone
);


--
-- Name: dict_engine_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.dict_engine ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.dict_engine_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: dict_machine; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dict_machine (
    id bigint NOT NULL,
    machine_brand character varying(200) NOT NULL,
    machine_model character varying(200),
    machine_name character varying(200),
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone,
    machine_category character varying(50) DEFAULT 'others'::character varying NOT NULL
);


--
-- Name: dict_machine_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.dict_machine ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.dict_machine_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: dict_media; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dict_media (
    id bigint NOT NULL,
    media_name character varying(100) NOT NULL,
    media_model character varying(100),
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone
);


--
-- Name: dict_media_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.dict_media ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.dict_media_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: dict_oem_no3; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dict_oem_no3 (
    id bigint NOT NULL,
    oem_no_3 character varying(200) NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone
);


--
-- Name: dict_oem_no3_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.dict_oem_no3 ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.dict_oem_no3_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: dict_product_name1; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dict_product_name1 (
    id bigint NOT NULL,
    product_name_1 character varying(200) NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone
);


--
-- Name: dict_product_name1_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.dict_product_name1 ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.dict_product_name1_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: dict_product_name2; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dict_product_name2 (
    id bigint NOT NULL,
    product_name_2 character varying(200) NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone
);


--
-- Name: dict_product_name2_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.dict_product_name2 ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.dict_product_name2_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: dict_type; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.dict_type (
    id bigint NOT NULL,
    type character varying(50) NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone
);


--
-- Name: dict_type_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.dict_type ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.dict_type_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: etl_progress_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.etl_progress_log (
    id bigint NOT NULL,
    entity_type character varying(20) NOT NULL,
    mode character varying(20) NOT NULL,
    file_path text NOT NULL,
    status character varying(20) NOT NULL,
    read_count bigint DEFAULT 0 NOT NULL,
    inserted_count bigint DEFAULT 0 NOT NULL,
    updated_count bigint DEFAULT 0 NOT NULL,
    skipped_count bigint DEFAULT 0 NOT NULL,
    skipped_missing_oem bigint DEFAULT 0 NOT NULL,
    skipped_null_field bigint DEFAULT 0 NOT NULL,
    skipped_duplicate bigint DEFAULT 0 NOT NULL,
    error_count bigint DEFAULT 0 NOT NULL,
    indexed_count bigint DEFAULT 0 NOT NULL,
    index_pending_count bigint DEFAULT 0 NOT NULL,
    last_error text,
    started_at timestamp with time zone NOT NULL,
    finished_at timestamp with time zone NOT NULL,
    duration_sec double precision NOT NULL,
    alert_sent boolean DEFAULT false NOT NULL,
    cancel_reason text,
    cancelled_at timestamp with time zone,
    reason_code character varying(32),
    checkpoint_id bigint,
    skipped_missing_mr1 bigint DEFAULT 0 NOT NULL
);


--
-- Name: TABLE etl_progress_log; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.etl_progress_log IS 'ETL 运行历史快照:Finish()/Fail() 时落库,用于运维回溯进程重启后的历史';


--
-- Name: COLUMN etl_progress_log.cancel_reason; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.etl_progress_log.cancel_reason IS 'Day 9.4: 取消 ETL 时记录的原因, NULL 表示非取消';


--
-- Name: COLUMN etl_progress_log.cancelled_at; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.etl_progress_log.cancelled_at IS 'Day 9.4: 取消时间, NULL 表示非取消';


--
-- Name: COLUMN etl_progress_log.reason_code; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.etl_progress_log.reason_code IS 'Day 9.5: 取消原因枚举码, NULL 表示非取消或旧记录';


--
-- Name: etl_progress_log_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.etl_progress_log_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: etl_progress_log_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.etl_progress_log_id_seq OWNED BY public.etl_progress_log.id;


--
-- Name: login_audit_logs; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.login_audit_logs (
    id bigint NOT NULL,
    user_id bigint,
    username character varying(64) NOT NULL,
    login_at timestamp with time zone DEFAULT now() NOT NULL,
    ip character varying(45),
    user_agent character varying(255),
    success boolean NOT NULL,
    failure_reason character varying(64)
);


--
-- Name: login_audit_logs_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.login_audit_logs ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.login_audit_logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: machine_applications; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.machine_applications (
    id bigint NOT NULL,
    product_id bigint NOT NULL,
    machine_brand character varying(200),
    machine_model character varying(200),
    model_name character varying(100),
    engine_brand character varying(100),
    engine_type character varying(100),
    engine_energy character varying(50),
    production_date_start date,
    is_ongoing boolean DEFAULT true,
    is_discontinued boolean DEFAULT false,
    created_at timestamp with time zone DEFAULT now(),
    production_date_end date,
    power character varying(50),
    serial_number_from character varying(50),
    serial_number_to character varying(50),
    car_body_type character varying(100),
    series character varying(100),
    co2_emission_standard character varying(50),
    transmission_type character varying(50),
    engine_displacement character varying(50),
    number_of_cylinders integer,
    gvwr character varying(50),
    tonnage character varying(50),
    geographic_area character varying(100),
    chassis_type character varying(100),
    engine_model character varying(100),
    cabin_type character varying(100),
    capacity character varying(50),
    engine_serial_number character varying(100),
    machine_category character varying(50) DEFAULT 'others'::character varying,
    CONSTRAINT chk_machine_apps_category CHECK (((machine_category IS NULL) OR ((machine_category)::text = ANY ((ARRAY['agriculture'::character varying, 'commercial'::character varying, 'construction'::character varying, 'industrial'::character varying, 'others'::character varying])::text[]))))
);


--
-- Name: machine_applications_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.machine_applications_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: machine_applications_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.machine_applications_id_seq OWNED BY public.machine_applications.id;


--
-- Name: product_history; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_history (
    id bigint NOT NULL,
    product_id bigint NOT NULL,
    change_type character varying(20),
    changed_fields jsonb,
    changed_by character varying(100),
    changed_at timestamp with time zone DEFAULT now()
);


--
-- Name: product_history_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.product_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: product_history_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.product_history_id_seq OWNED BY public.product_history.id;


--
-- Name: product_images; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_images (
    id bigint NOT NULL,
    product_id bigint NOT NULL,
    slot smallint NOT NULL,
    image_key character varying(500) NOT NULL,
    file_size bigint,
    content_type character varying(50),
    width integer,
    height integer,
    is_primary boolean DEFAULT false NOT NULL,
    display_order integer DEFAULT 0 NOT NULL,
    uploaded_at timestamp with time zone DEFAULT now() NOT NULL,
    uploaded_by character varying(100),
    image_role character varying(20) DEFAULT 'detail'::character varying NOT NULL,
    oem_no_3 character varying(200),
    CONSTRAINT chk_image_role CHECK (((image_role)::text = ANY ((ARRAY['primary'::character varying, 'detail'::character varying])::text[]))),
    CONSTRAINT chk_image_role_slot CHECK (((((image_role)::text = 'primary'::text) AND (slot = 1)) OR (((image_role)::text = 'detail'::text) AND ((slot >= 2) AND (slot <= 6))))),
    CONSTRAINT product_images_slot_check CHECK (((slot >= 1) AND (slot <= 6)))
);


--
-- Name: product_images_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.product_images_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: product_images_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.product_images_id_seq OWNED BY public.product_images.id;


--
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    id bigint NOT NULL,
    oem_no_normalized character varying(50),
    oem_no_display character varying(50) NOT NULL,
    remark text,
    product_name_3 character varying(100),
    type character varying(50) NOT NULL,
    d1_mm numeric(10,2),
    d2_mm numeric(10,2),
    d3_mm numeric(10,2),
    h1_mm numeric(10,2),
    h2_mm numeric(10,2),
    h3_mm numeric(10,2),
    d7_thread character varying(50),
    d8_thread character varying(50),
    media character varying(100),
    sealing_material character varying(100),
    efficiency_1 character varying(100),
    bypass_valve_lr numeric,
    collapse_pressure_bar numeric,
    temp_range character varying(50),
    qty_per_carton integer,
    weight_kgs numeric(8,3),
    carton_length_mm numeric(8,2),
    carton_width_mm numeric(8,2),
    carton_height_mm numeric(8,2),
    image_key character varying(500),
    image_status character varying(20) DEFAULT 'pending'::character varying,
    is_discontinued boolean DEFAULT false,
    discontinued_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now(),
    updated_at timestamp with time zone DEFAULT now(),
    efficiency_2 character varying(100),
    bypass_valve_hr numeric,
    bypass_pressure numeric,
    product_name_1 character varying(100),
    product_name_2 character varying(100),
    mr_1 character varying(10),
    oem_2 character varying(50),
    is_published boolean DEFAULT true NOT NULL,
    h4_mm numeric(10,2),
    d4_mm numeric(10,2),
    no_check_valves integer,
    no_bypass_valves integer,
    media_model character varying(200),
    master_box_qty integer,
    master_box_weight_kgs numeric(10,2),
    master_box_length_mm numeric(10,2),
    master_box_width_mm numeric(10,2),
    master_box_height_mm numeric(10,2),
    volume_per_carton_m3 numeric(12,6),
    d1_mm_raw text,
    d2_mm_raw text,
    d3_mm_raw text,
    d4_mm_raw text,
    h1_mm_raw text,
    h2_mm_raw text,
    h3_mm_raw text,
    h4_mm_raw text
);


--
-- Name: products_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.products_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: products_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.products_id_seq OWNED BY public.products.id;


--
-- Name: refresh_tokens; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.refresh_tokens (
    id bigint NOT NULL,
    user_id bigint NOT NULL,
    token_hash character varying(255) NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone,
    replaced_by_token_id bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_ip character varying(45)
);


--
-- Name: refresh_tokens_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.refresh_tokens ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.refresh_tokens_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: search_index_dead_letter; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.search_index_dead_letter (
    id bigint NOT NULL,
    original_id bigint NOT NULL,
    operation character varying(20) NOT NULL,
    payload jsonb NOT NULL,
    retry_count integer DEFAULT 5 NOT NULL,
    last_error text,
    created_at timestamp with time zone NOT NULL,
    moved_at timestamp with time zone DEFAULT now() NOT NULL,
    recovery_count integer DEFAULT 0 NOT NULL,
    last_recovery_at timestamp with time zone,
    last_recovery_error text,
    status character varying(20) DEFAULT 'active'::character varying NOT NULL,
    recovered_at timestamp with time zone,
    recovered_to_pending_id bigint
);


--
-- Name: TABLE search_index_dead_letter; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.search_index_dead_letter IS 'Meili 索引写入死信队列:retry 5 次仍失败,IndexReplayWorker 自动转入,需人工排查';


--
-- Name: COLUMN search_index_dead_letter.recovery_count; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_dead_letter.recovery_count IS '自动恢复次数,超过 max_recovery_count (默认 3) 后不再自动重试,需人工 recover';


--
-- Name: COLUMN search_index_dead_letter.last_recovery_at; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_dead_letter.last_recovery_at IS '最近一次自动恢复时间,用于冷却 (默认 10min)';


--
-- Name: COLUMN search_index_dead_letter.last_recovery_error; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_dead_letter.last_recovery_error IS '自动恢复过程中遇到的错误,例如 Meili 仍不可用; 排查"为什么自动恢复不工作"用';


--
-- Name: COLUMN search_index_dead_letter.status; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_dead_letter.status IS 'active = 等待处理; recovered = 已恢复到 pending, 历史留痕, 不参与 worker 扫描';


--
-- Name: COLUMN search_index_dead_letter.recovered_at; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_dead_letter.recovered_at IS '最近一次恢复到 pending 的时间, 跨循环保留 recovery_count 关联';


--
-- Name: COLUMN search_index_dead_letter.recovered_to_pending_id; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_dead_letter.recovered_to_pending_id IS '恢复到 search_index_pending 的新行 id, 便于链路追踪';


--
-- Name: search_index_dead_letter_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.search_index_dead_letter_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: search_index_dead_letter_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.search_index_dead_letter_id_seq OWNED BY public.search_index_dead_letter.id;


--
-- Name: search_index_pending; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.search_index_pending (
    id bigint NOT NULL,
    operation character varying(20) NOT NULL,
    payload jsonb NOT NULL,
    retry_count integer DEFAULT 0 NOT NULL,
    last_error text,
    created_at timestamp with time zone DEFAULT now(),
    next_retry_at timestamp with time zone DEFAULT now()
);


--
-- Name: TABLE search_index_pending; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.search_index_pending IS 'Meili 索引写入补偿队列,失败重试 60s 一次';


--
-- Name: COLUMN search_index_pending.operation; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_pending.operation IS 'index=写入/更新, delete=删除';


--
-- Name: COLUMN search_index_pending.payload; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_pending.payload IS 'JSON: index=ProductIndexDoc, delete=[id1,id2,...]';


--
-- Name: COLUMN search_index_pending.next_retry_at; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.search_index_pending.next_retry_at IS '下次重试时间,失败后指数退避 60s/120s/300s';


--
-- Name: search_index_pending_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.search_index_pending_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: search_index_pending_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.search_index_pending_id_seq OWNED BY public.search_index_pending.id;


--
-- Name: security_events; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.security_events (
    id bigint NOT NULL,
    event_type character varying(64) NOT NULL,
    user_id bigint,
    username character varying(64),
    ip character varying(45),
    user_agent character varying(255),
    details jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: security_events_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.security_events_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: security_events_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.security_events_id_seq OWNED BY public.security_events.id;


--
-- Name: system_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.system_settings (
    key character varying(100) NOT NULL,
    value text,
    description text,
    updated_at timestamp with time zone DEFAULT now()
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    id bigint NOT NULL,
    username character varying(64) NOT NULL,
    email character varying(128),
    password_hash character varying(255) NOT NULL,
    full_name character varying(64),
    role character varying(16) DEFAULT 'viewer'::character varying NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    failed_login_count integer DEFAULT 0 NOT NULL,
    locked_until timestamp with time zone,
    last_login_at timestamp with time zone,
    last_login_ip character varying(45),
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    deleted_at timestamp with time zone
);


--
-- Name: users_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.users ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.users_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: xref_oem_brand; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.xref_oem_brand (
    id bigint NOT NULL,
    brand character varying(100) NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp without time zone DEFAULT now() NOT NULL,
    updated_at timestamp without time zone DEFAULT now() NOT NULL,
    deleted_at timestamp without time zone
);


--
-- Name: xref_oem_brand_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.xref_oem_brand ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.xref_oem_brand_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: xrefs_stage; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.xrefs_stage (
    product_id bigint,
    product_name_1 character varying(100),
    oem_brand character varying(100),
    oem_no_3 character varying(100)
);


--
-- Name: alert_history id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert_history ALTER COLUMN id SET DEFAULT nextval('public.alert_history_id_seq'::regclass);


--
-- Name: alert_rules id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert_rules ALTER COLUMN id SET DEFAULT nextval('public.alert_rules_id_seq'::regclass);


--
-- Name: cross_references id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cross_references ALTER COLUMN id SET DEFAULT nextval('public.cross_references_id_seq'::regclass);


--
-- Name: etl_progress_log id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etl_progress_log ALTER COLUMN id SET DEFAULT nextval('public.etl_progress_log_id_seq'::regclass);


--
-- Name: machine_applications id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.machine_applications ALTER COLUMN id SET DEFAULT nextval('public.machine_applications_id_seq'::regclass);


--
-- Name: product_history id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_history ALTER COLUMN id SET DEFAULT nextval('public.product_history_id_seq'::regclass);


--
-- Name: product_images id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images ALTER COLUMN id SET DEFAULT nextval('public.product_images_id_seq'::regclass);


--
-- Name: products id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products ALTER COLUMN id SET DEFAULT nextval('public.products_id_seq'::regclass);


--
-- Name: search_index_dead_letter id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.search_index_dead_letter ALTER COLUMN id SET DEFAULT nextval('public.search_index_dead_letter_id_seq'::regclass);


--
-- Name: search_index_pending id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.search_index_pending ALTER COLUMN id SET DEFAULT nextval('public.search_index_pending_id_seq'::regclass);


--
-- Name: security_events id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.security_events ALTER COLUMN id SET DEFAULT nextval('public.security_events_id_seq'::regclass);


--
-- Name: __EFMigrationsHistory __EFMigrationsHistory_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public."__EFMigrationsHistory"
    ADD CONSTRAINT "__EFMigrationsHistory_pkey" PRIMARY KEY (migration_id);


--
-- Name: alert_history alert_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert_history
    ADD CONSTRAINT alert_history_pkey PRIMARY KEY (id);


--
-- Name: alert_rules alert_rules_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert_rules
    ADD CONSTRAINT alert_rules_pkey PRIMARY KEY (id);


--
-- Name: alert_rules alert_rules_type_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.alert_rules
    ADD CONSTRAINT alert_rules_type_key UNIQUE (type);


--
-- Name: auth_token_state auth_token_state_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.auth_token_state
    ADD CONSTRAINT auth_token_state_pkey PRIMARY KEY (id);


--
-- Name: cross_references cross_references_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cross_references
    ADD CONSTRAINT cross_references_pkey PRIMARY KEY (id);


--
-- Name: etl_progress_log etl_progress_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.etl_progress_log
    ADD CONSTRAINT etl_progress_log_pkey PRIMARY KEY (id);


--
-- Name: machine_applications machine_applications_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.machine_applications
    ADD CONSTRAINT machine_applications_pkey PRIMARY KEY (id);


--
-- Name: dict_engine pk_dict_engine; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dict_engine
    ADD CONSTRAINT pk_dict_engine PRIMARY KEY (id);


--
-- Name: dict_machine pk_dict_machine; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dict_machine
    ADD CONSTRAINT pk_dict_machine PRIMARY KEY (id);


--
-- Name: dict_media pk_dict_media; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dict_media
    ADD CONSTRAINT pk_dict_media PRIMARY KEY (id);


--
-- Name: dict_oem_no3 pk_dict_oem_no3; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dict_oem_no3
    ADD CONSTRAINT pk_dict_oem_no3 PRIMARY KEY (id);


--
-- Name: dict_product_name1 pk_dict_product_name1; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dict_product_name1
    ADD CONSTRAINT pk_dict_product_name1 PRIMARY KEY (id);


--
-- Name: dict_product_name2 pk_dict_product_name2; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dict_product_name2
    ADD CONSTRAINT pk_dict_product_name2 PRIMARY KEY (id);


--
-- Name: dict_type pk_dict_type; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.dict_type
    ADD CONSTRAINT pk_dict_type PRIMARY KEY (id);


--
-- Name: login_audit_logs pk_login_audit_logs; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.login_audit_logs
    ADD CONSTRAINT pk_login_audit_logs PRIMARY KEY (id);


--
-- Name: refresh_tokens pk_refresh_tokens; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT pk_refresh_tokens PRIMARY KEY (id);


--
-- Name: users pk_users; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT pk_users PRIMARY KEY (id);


--
-- Name: xref_oem_brand pk_xref_oem_brand; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.xref_oem_brand
    ADD CONSTRAINT pk_xref_oem_brand PRIMARY KEY (id);


--
-- Name: product_history product_history_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_history
    ADD CONSTRAINT product_history_pkey PRIMARY KEY (id);


--
-- Name: product_images product_images_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_pkey PRIMARY KEY (id);


--
-- Name: product_images product_images_product_id_slot_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_product_id_slot_key UNIQUE (product_id, slot);


--
-- Name: products products_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (id);


--
-- Name: search_index_dead_letter search_index_dead_letter_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.search_index_dead_letter
    ADD CONSTRAINT search_index_dead_letter_pkey PRIMARY KEY (id);


--
-- Name: search_index_pending search_index_pending_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.search_index_pending
    ADD CONSTRAINT search_index_pending_pkey PRIMARY KEY (id);


--
-- Name: security_events security_events_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.security_events
    ADD CONSTRAINT security_events_pkey PRIMARY KEY (id);


--
-- Name: system_settings system_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.system_settings
    ADD CONSTRAINT system_settings_pkey PRIMARY KEY (key);


--
-- Name: idx_alert_history_correlation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_alert_history_correlation ON public.alert_history USING btree (correlation_id) WHERE (correlation_id IS NOT NULL);


--
-- Name: idx_alert_history_severity_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_alert_history_severity_time ON public.alert_history USING btree (severity, sent_at DESC);


--
-- Name: idx_alert_history_status_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_alert_history_status_time ON public.alert_history USING btree (status, sent_at DESC) WHERE ((status)::text = ANY ((ARRAY['failed'::character varying, 'suppressed'::character varying])::text[]));


--
-- Name: idx_alert_history_type_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_alert_history_type_time ON public.alert_history USING btree (type, sent_at DESC);


--
-- Name: idx_app_brand_model; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_app_brand_model ON public.machine_applications USING btree (machine_brand, machine_model);


--
-- Name: idx_app_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_app_product ON public.machine_applications USING btree (product_id);


--
-- Name: idx_dead_letter_active_recovery; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dead_letter_active_recovery ON public.search_index_dead_letter USING btree (status, recovery_count, last_recovery_at) WHERE ((status)::text = 'active'::text);


--
-- Name: idx_dead_letter_moved_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dead_letter_moved_at ON public.search_index_dead_letter USING btree (moved_at DESC);


--
-- Name: idx_dead_letter_operation; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dead_letter_operation ON public.search_index_dead_letter USING btree (operation);


--
-- Name: idx_dead_letter_payload_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dead_letter_payload_hash ON public.search_index_dead_letter USING btree (operation, md5((payload)::text), status);


--
-- Name: idx_dead_letter_recovered_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dead_letter_recovered_at ON public.search_index_dead_letter USING btree (recovered_at) WHERE ((status)::text = 'recovered'::text);


--
-- Name: idx_dead_letter_recovery; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dead_letter_recovery ON public.search_index_dead_letter USING btree (recovery_count, last_recovery_at);


--
-- Name: idx_dict_engine_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_engine_active ON public.dict_engine USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_machine_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_machine_active ON public.dict_machine USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_machine_category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_machine_category ON public.dict_machine USING btree (deleted_at, machine_category) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_media_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_media_active ON public.dict_media USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_oem_no3_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_oem_no3_active ON public.dict_oem_no3 USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_oem_no3_sort; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_oem_no3_sort ON public.dict_oem_no3 USING btree (sort_order, oem_no_3) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_product_name1_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_product_name1_active ON public.dict_product_name1 USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_product_name2_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_product_name2_active ON public.dict_product_name2 USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_dict_type_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_dict_type_active ON public.dict_type USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_etl_log_entity_finished; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etl_log_entity_finished ON public.etl_progress_log USING btree (entity_type, finished_at DESC);


--
-- Name: idx_etl_log_failed_unalerted; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etl_log_failed_unalerted ON public.etl_progress_log USING btree (id) WHERE (((status)::text = 'failed'::text) AND (alert_sent = false));


--
-- Name: idx_etl_log_finished; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etl_log_finished ON public.etl_progress_log USING btree (finished_at DESC);


--
-- Name: idx_etl_log_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_etl_log_status ON public.etl_progress_log USING btree (status);


--
-- Name: idx_login_audit_user_login_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_login_audit_user_login_at ON public.login_audit_logs USING btree (user_id, login_at);


--
-- Name: idx_machine_apps_category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_machine_apps_category ON public.machine_applications USING btree (machine_category, machine_brand, machine_model);


--
-- Name: idx_pending_retry; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_pending_retry ON public.search_index_pending USING btree (next_retry_at, retry_count);


--
-- Name: idx_product_history_paging; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_product_history_paging ON public.product_history USING btree (product_id, changed_at DESC, id DESC);


--
-- Name: idx_product_images_product_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_product_images_product_id ON public.product_images USING btree (product_id);


--
-- Name: idx_products_d1; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_d1 ON public.products USING btree (d1_mm);


--
-- Name: idx_products_d2; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_d2 ON public.products USING btree (d2_mm);


--
-- Name: idx_products_h1; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_h1 ON public.products USING btree (h1_mm);


--
-- Name: idx_products_is_published_true; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_is_published_true ON public.products USING btree (id) WHERE (is_published = true);


--
-- Name: idx_products_mr_1; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_mr_1 ON public.products USING btree (mr_1);


--
-- Name: idx_products_oem_2; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_oem_2 ON public.products USING btree (oem_2);


--
-- Name: idx_products_oem_disp; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_oem_disp ON public.products USING btree (oem_no_display);


--
-- Name: idx_products_oem_no_normalized; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_oem_no_normalized ON public.products USING btree (oem_no_normalized) WHERE (oem_no_normalized IS NOT NULL);


--
-- Name: idx_products_oem_norm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_oem_norm ON public.products USING btree (oem_no_normalized);


--
-- Name: idx_products_product_name_1; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_product_name_1 ON public.products USING btree (product_name_1);


--
-- Name: idx_products_type; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_type ON public.products USING btree (type);


--
-- Name: idx_products_type_d1; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_type_d1 ON public.products USING btree (type, d1_mm);


--
-- Name: idx_products_type_d2; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_type_d2 ON public.products USING btree (type, d2_mm);


--
-- Name: idx_products_type_h1; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_type_h1 ON public.products USING btree (type, h1_mm);


--
-- Name: idx_security_events_ip_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_security_events_ip_time ON public.security_events USING btree (ip, created_at DESC) WHERE (ip IS NOT NULL);


--
-- Name: idx_security_events_type_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_security_events_type_time ON public.security_events USING btree (event_type, created_at DESC);


--
-- Name: idx_security_events_user_time; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_security_events_user_time ON public.security_events USING btree (user_id, created_at DESC) WHERE (user_id IS NOT NULL);


--
-- Name: idx_users_deleted_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_users_deleted_at ON public.users USING btree (deleted_at) WHERE (deleted_at IS NULL);


--
-- Name: idx_xref_oem_brand_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_xref_oem_brand_active ON public.xref_oem_brand USING btree (deleted_at, sort_order) WHERE (deleted_at IS NULL);


--
-- Name: idx_xref_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_xref_product ON public.cross_references USING btree (product_id);


--
-- Name: idx_xrefs_brand_oem3_sort; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_xrefs_brand_oem3_sort ON public.cross_references USING btree (oem_brand, sort_order, oem_no_3) WHERE ((is_discontinued = false) AND (is_published = true));


--
-- Name: ix_alert_rules_type; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_alert_rules_type ON public.alert_rules USING btree (type);


--
-- Name: ix_dict_engine_engine_brand_engine_type; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_dict_engine_engine_brand_engine_type ON public.dict_engine USING btree (engine_brand, engine_type);


--
-- Name: ix_dict_machine_machine_brand_machine_model_machine_name; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_dict_machine_machine_brand_machine_model_machine_name ON public.dict_machine USING btree (machine_brand, machine_model, machine_name);


--
-- Name: ix_dict_media_media_name_media_model; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_dict_media_media_name_media_model ON public.dict_media USING btree (media_name, media_model);


--
-- Name: ix_dict_oem_no3_oem_no_3; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_dict_oem_no3_oem_no_3 ON public.dict_oem_no3 USING btree (oem_no_3);


--
-- Name: ix_dict_product_name1_product_name_1; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_dict_product_name1_product_name_1 ON public.dict_product_name1 USING btree (product_name_1);


--
-- Name: ix_dict_product_name2_product_name_2; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_dict_product_name2_product_name_2 ON public.dict_product_name2 USING btree (product_name_2);


--
-- Name: ix_dict_type_type; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_dict_type_type ON public.dict_type USING btree (type);


--
-- Name: ix_login_audit_logs_login_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_login_audit_logs_login_at ON public.login_audit_logs USING btree (login_at);


--
-- Name: ix_machine_apps_brand_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_machine_apps_brand_trgm ON public.machine_applications USING gin (machine_brand public.gin_trgm_ops);


--
-- Name: ix_machine_apps_model_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_machine_apps_model_trgm ON public.machine_applications USING gin (machine_model public.gin_trgm_ops);


--
-- Name: ix_products_mr_1_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_products_mr_1_trgm ON public.products USING gin (mr_1 public.gin_trgm_ops);


--
-- Name: ix_products_oem_2_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_products_oem_2_trgm ON public.products USING gin (oem_2 public.gin_trgm_ops);


--
-- Name: ix_products_product_name_1_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_products_product_name_1_trgm ON public.products USING gin (product_name_1 public.gin_trgm_ops);


--
-- Name: ix_products_product_name_2_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_products_product_name_2_trgm ON public.products USING gin (product_name_2 public.gin_trgm_ops);


--
-- Name: ix_products_remark_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_products_remark_trgm ON public.products USING gin (remark public.gin_trgm_ops);


--
-- Name: ix_refresh_tokens_token_hash; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON public.refresh_tokens USING btree (token_hash);


--
-- Name: ix_refresh_tokens_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_refresh_tokens_user_id ON public.refresh_tokens USING btree (user_id);


--
-- Name: ix_users_username; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_users_username ON public.users USING btree (username);


--
-- Name: ix_xref_oem_brand_brand; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ix_xref_oem_brand_brand ON public.xref_oem_brand USING btree (brand);


--
-- Name: ix_xrefs_oem_2_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_xrefs_oem_2_trgm ON public.cross_references USING gin (oem_2 public.gin_trgm_ops);


--
-- Name: ix_xrefs_oem_brand_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_xrefs_oem_brand_trgm ON public.cross_references USING gin (oem_brand public.gin_trgm_ops);


--
-- Name: ix_xrefs_oem_no_3_trgm; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_xrefs_oem_no_3_trgm ON public.cross_references USING gin (oem_no_3 public.gin_trgm_ops);


--
-- Name: uq_apps_product_brand_model; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_apps_product_brand_model ON public.machine_applications USING btree (product_id, machine_brand, machine_model) WHERE ((machine_brand IS NOT NULL) AND (machine_model IS NOT NULL));


--
-- Name: uq_product_images_detail_slot; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_product_images_detail_slot ON public.product_images USING btree (product_id, slot) WHERE ((image_role)::text = 'detail'::text);


--
-- Name: uq_product_images_primary; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_product_images_primary ON public.product_images USING btree (product_id) WHERE (is_primary = true);


--
-- Name: uq_products_oem_normalized; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX uq_products_oem_normalized ON public.products USING btree (oem_no_normalized);


--
-- Name: refresh_tokens fk_refresh_tokens_users_user_id; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: product_images product_images_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

