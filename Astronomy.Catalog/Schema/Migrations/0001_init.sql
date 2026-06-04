-- 0001_init.sql — initial Catalog.db schema.
-- Follows the IS schema rules R1-R12: GUID BLOB(16) PKs, snake_case, NULL (not sentinels),
-- enum lookup tables + CHECK, every FK indexed, INTEGER UNIX-seconds timestamps.
-- WAL / synchronous / foreign_keys are set by SchemaManager on open, not here.

-- ----- Lookup tables (R4) ---------------------------------------------------

CREATE TABLE project_state (
    id   INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
) WITHOUT ROWID;
INSERT INTO project_state (id, name) VALUES (0, 'Draft'), (1, 'Active'), (2, 'Inactive'), (3, 'Closed');

CREATE TABLE project_priority (
    id   INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
) WITHOUT ROWID;
INSERT INTO project_priority (id, name) VALUES (0, 'Low'), (1, 'Normal'), (2, 'High');

CREATE TABLE epoch (
    id   INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
) WITHOUT ROWID;
INSERT INTO epoch (id, name) VALUES (0, 'B1950'), (1, 'JNow'), (2, 'J2000');

CREATE TABLE frame_type (
    id   INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
) WITHOUT ROWID;
INSERT INTO frame_type (id, name) VALUES (0, 'Light'), (1, 'Dark'), (2, 'Flat'), (3, 'Bias');

CREATE TABLE processing_stage (
    id   INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
) WITHOUT ROWID;
INSERT INTO processing_stage (id, name)
VALUES (0, 'Captures'), (1, 'Calibrated'), (2, 'Cosmetized'), (3, 'Debayered'), (4, 'Master'), (5, 'Integrated');

-- ----- Plan plane (cleaned from TS) -----------------------------------------

CREATE TABLE profile (
    id                BLOB NOT NULL PRIMARY KEY,        -- 16-byte big-endian GUID
    name              TEXT NOT NULL,
    nina_profile_guid TEXT,                             -- correlate back to a NINA profile, if known
    created_at        INTEGER NOT NULL                  -- UNIX seconds
) WITHOUT ROWID;

CREATE TABLE project (
    id                      BLOB NOT NULL PRIMARY KEY,
    profile_id              BLOB NOT NULL REFERENCES profile(id),
    name                    TEXT NOT NULL,
    description             TEXT,
    state_id                INTEGER NOT NULL REFERENCES project_state(id),
    priority_id             INTEGER NOT NULL REFERENCES project_priority(id),
    minimum_altitude_deg    REAL,                        -- NULL = no floor
    maximum_altitude_deg    REAL,                        -- NULL = no ceiling
    minimum_time_minutes    INTEGER,
    use_custom_horizon      INTEGER NOT NULL DEFAULT 0 CHECK (use_custom_horizon IN (0, 1)),
    horizon_offset_deg      REAL,
    meridian_window_minutes INTEGER,
    is_mosaic               INTEGER NOT NULL DEFAULT 0 CHECK (is_mosaic IN (0, 1)),
    enable_grader           INTEGER NOT NULL DEFAULT 1 CHECK (enable_grader IN (0, 1)),
    created_at              INTEGER NOT NULL,
    active_at               INTEGER,                     -- NULL = never activated
    inactive_at             INTEGER,                     -- NULL = still active
    imported_from_ts_guid   TEXT                         -- provenance for one-time TS import (Phase 2)
) WITHOUT ROWID;
CREATE INDEX ix_project_profile_id ON project(profile_id);
CREATE INDEX ix_project_profile_state ON project(profile_id, state_id);

CREATE TABLE target (
    id                    BLOB NOT NULL PRIMARY KEY,
    project_id            BLOB NOT NULL REFERENCES project(id),
    name                  TEXT NOT NULL,
    enabled               INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    ra_hours              REAL CHECK (ra_hours IS NULL OR (ra_hours >= 0.0 AND ra_hours < 24.0)),
    dec_degrees_signed    REAL CHECK (dec_degrees_signed IS NULL OR (dec_degrees_signed >= -90.0 AND dec_degrees_signed <= 90.0)),
    epoch_id              INTEGER NOT NULL DEFAULT 2 REFERENCES epoch(id),
    rotation_deg          REAL,
    roi_percent           REAL,
    priority_id           INTEGER REFERENCES project_priority(id),  -- NULL = inherit project priority (R3)
    created_at            INTEGER NOT NULL,
    imported_from_ts_guid TEXT
) WITHOUT ROWID;
CREATE INDEX ix_target_project_id ON target(project_id);

CREATE TABLE exposure_template (
    id                       BLOB NOT NULL PRIMARY KEY,
    profile_id               BLOB NOT NULL REFERENCES profile(id),
    name                     TEXT NOT NULL,
    filter_name              TEXT NOT NULL,
    gain                     INTEGER,
    offset_adu               INTEGER,                    -- "offset" is a SQL keyword; avoid quoting (R7)
    binning                  INTEGER,
    readout_mode             INTEGER,
    default_exposure_seconds REAL,
    imported_from_ts_guid    TEXT
) WITHOUT ROWID;
CREATE INDEX ix_exposure_template_profile_id ON exposure_template(profile_id);

CREATE TABLE exposure_plan (
    id                    BLOB NOT NULL PRIMARY KEY,
    target_id             BLOB NOT NULL REFERENCES target(id),
    exposure_template_id  BLOB NOT NULL REFERENCES exposure_template(id),
    exposure_seconds      REAL,                          -- NULL = inherit template default (R3)
    desired_count         INTEGER NOT NULL DEFAULT 0,    -- the "goal"
    acquired_count        INTEGER NOT NULL DEFAULT 0,
    accepted_count        INTEGER NOT NULL DEFAULT 0,
    enabled               INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    imported_from_ts_guid TEXT
) WITHOUT ROWID;
CREATE INDEX ix_exposure_plan_target_id ON exposure_plan(target_id);
CREATE INDEX ix_exposure_plan_template_id ON exposure_plan(exposure_template_id);

-- ----- Inventory plane (disk-derived; populated by the Phase 2 scanner) ------

CREATE TABLE image_file (
    id                  BLOB NOT NULL PRIMARY KEY,
    path                TEXT NOT NULL UNIQUE,
    target_id           BLOB REFERENCES target(id),      -- NULL until resolved to a plan target
    target_name         TEXT,                            -- as read from disk/header before resolution
    filter_name         TEXT,
    frame_type_id       INTEGER REFERENCES frame_type(id),
    processing_stage_id INTEGER REFERENCES processing_stage(id),
    exposure_seconds    REAL,
    captured_at         INTEGER,                         -- UNIX seconds
    camera              TEXT,
    gain                INTEGER,
    offset_adu          INTEGER,
    ra_hours            REAL,
    dec_degrees_signed  REAL,
    file_mtime          INTEGER NOT NULL,
    file_size           INTEGER NOT NULL,
    scanned_at          INTEGER NOT NULL
) WITHOUT ROWID;
CREATE INDEX ix_image_file_target_id ON image_file(target_id);
CREATE INDEX ix_image_file_filter ON image_file(filter_name);
CREATE INDEX ix_image_file_target_filter_stage ON image_file(target_id, filter_name, processing_stage_id);

CREATE TABLE scan_state (
    folder          TEXT NOT NULL PRIMARY KEY,           -- target folder (or scan root)
    last_scanned_at INTEGER NOT NULL,
    max_mtime_seen  INTEGER NOT NULL,                    -- watermark for incremental re-scan
    file_count      INTEGER NOT NULL
) WITHOUT ROWID;

-- Always-consistent aggregate of light-frame integration per target/filter/stage.
CREATE VIEW inventory_rollup AS
    SELECT target_id,
           target_name,
           filter_name,
           processing_stage_id,
           COUNT(*)                          AS frame_count,
           COALESCE(SUM(exposure_seconds), 0.0) AS total_integration_seconds,
           MIN(captured_at)                  AS first_captured_at,
           MAX(captured_at)                  AS last_captured_at
    FROM image_file
    WHERE frame_type_id = 0   -- lights only
    GROUP BY target_id, target_name, filter_name, processing_stage_id;
