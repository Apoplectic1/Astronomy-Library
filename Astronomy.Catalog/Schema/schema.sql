-- schema.sql — full Catalog.db schema.
-- No migration framework: the catalog is fully DERIVED (disk scan + TS import; goals live in the scheduler DB),
-- so it is always rebuildable. Applied idempotently on open (CREATE TABLE IF NOT EXISTS + INSERT OR IGNORE);
-- a schema change just means deleting the regenerable Catalog.db. WAL / foreign_keys / synchronous are set by
-- SchemaManager on open. Rules: GUID BLOB(16) PKs, snake_case, NULL-not-sentinel, enum lookup tables, indexed FKs.

-- ----- Lookup tables --------------------------------------------------------

CREATE TABLE IF NOT EXISTS project_state (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO project_state (id, name) VALUES (0, 'Draft'), (1, 'Active'), (2, 'Inactive'), (3, 'Closed');

CREATE TABLE IF NOT EXISTS project_priority (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO project_priority (id, name) VALUES (0, 'Low'), (1, 'Normal'), (2, 'High');

CREATE TABLE IF NOT EXISTS epoch (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO epoch (id, name) VALUES (0, 'B1950'), (1, 'JNow'), (2, 'J2000');

CREATE TABLE IF NOT EXISTS frame_purpose (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO frame_purpose (id, name) VALUES (0, 'Light'), (1, 'Stars');

-- ----- Plan plane (imported from TS; goals = exposure_plan.desired_count) ----

CREATE TABLE IF NOT EXISTS profile (
    id                BLOB NOT NULL PRIMARY KEY,
    name              TEXT NOT NULL,
    nina_profile_guid TEXT,
    created_at        INTEGER NOT NULL
) WITHOUT ROWID;

CREATE TABLE IF NOT EXISTS project (
    id                      BLOB NOT NULL PRIMARY KEY,
    profile_id              BLOB NOT NULL REFERENCES profile(id),
    name                    TEXT NOT NULL,
    description             TEXT,
    state_id                INTEGER NOT NULL REFERENCES project_state(id),
    priority_id             INTEGER NOT NULL REFERENCES project_priority(id),
    minimum_altitude_deg    REAL,
    maximum_altitude_deg    REAL,
    minimum_time_minutes    INTEGER,
    use_custom_horizon      INTEGER NOT NULL DEFAULT 0 CHECK (use_custom_horizon IN (0, 1)),
    horizon_offset_deg      REAL,
    meridian_window_minutes INTEGER,
    is_mosaic               INTEGER NOT NULL DEFAULT 0 CHECK (is_mosaic IN (0, 1)),
    enable_grader           INTEGER NOT NULL DEFAULT 1 CHECK (enable_grader IN (0, 1)),
    created_at              INTEGER NOT NULL,
    active_at               INTEGER,
    inactive_at             INTEGER,
    imported_from_ts_guid   TEXT
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS ix_project_profile_id ON project(profile_id);
CREATE INDEX IF NOT EXISTS ix_project_profile_state ON project(profile_id, state_id);

CREATE TABLE IF NOT EXISTS target (
    id                    BLOB NOT NULL PRIMARY KEY,
    project_id            BLOB NOT NULL REFERENCES project(id),
    name                  TEXT NOT NULL,
    enabled               INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    ra_hours              REAL CHECK (ra_hours IS NULL OR (ra_hours >= 0.0 AND ra_hours < 24.0)),
    dec_degrees_signed    REAL CHECK (dec_degrees_signed IS NULL OR (dec_degrees_signed >= -90.0 AND dec_degrees_signed <= 90.0)),
    epoch_id              INTEGER NOT NULL DEFAULT 2 REFERENCES epoch(id),
    rotation_deg          REAL,
    roi_percent           REAL,
    priority_id           INTEGER REFERENCES project_priority(id),
    created_at            INTEGER NOT NULL,
    imported_from_ts_guid TEXT
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS ix_target_project_id ON target(project_id);

CREATE TABLE IF NOT EXISTS exposure_template (
    id                       BLOB NOT NULL PRIMARY KEY,
    profile_id               BLOB NOT NULL REFERENCES profile(id),
    name                     TEXT NOT NULL,
    filter_name              TEXT NOT NULL,
    gain                     INTEGER,
    offset_adu               INTEGER,
    binning                  INTEGER,
    readout_mode             INTEGER,
    default_exposure_seconds REAL,
    imported_from_ts_guid    TEXT
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS ix_exposure_template_profile_id ON exposure_template(profile_id);

CREATE TABLE IF NOT EXISTS exposure_plan (
    id                    BLOB NOT NULL PRIMARY KEY,
    target_id             BLOB NOT NULL REFERENCES target(id),
    exposure_template_id  BLOB NOT NULL REFERENCES exposure_template(id),
    exposure_seconds      REAL,
    desired_count         INTEGER NOT NULL DEFAULT 0,
    acquired_count        INTEGER NOT NULL DEFAULT 0,
    accepted_count        INTEGER NOT NULL DEFAULT 0,
    enabled               INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    imported_from_ts_guid TEXT
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS ix_exposure_plan_target_id ON exposure_plan(target_id);
CREATE INDEX IF NOT EXISTS ix_exposure_plan_template_id ON exposure_plan(exposure_template_id);

-- ----- Inventory plane (derived; persisted ImageLibraryScanner aggregates) ---

CREATE TABLE IF NOT EXISTS inventory_target (
    directory_name     TEXT NOT NULL PRIMARY KEY,   -- canonical identity, e.g. "M51 - Whirlpool"
    catalog            TEXT NOT NULL,                -- before " - "
    common_name        TEXT,                         -- after " - " (NULL if none)
    object_name        TEXT NOT NULL,                -- FITS OBJECT consensus
    ra_hours           REAL NOT NULL,                -- decimal hours [0,24)
    dec_degrees_signed REAL NOT NULL,                -- signed degrees [-90,90]
    scanned_at         INTEGER NOT NULL              -- UNIX seconds of the scan
) WITHOUT ROWID;

CREATE TABLE IF NOT EXISTS inventory_filter (
    directory_name            TEXT NOT NULL REFERENCES inventory_target(directory_name) ON DELETE CASCADE,
    filter_code               TEXT NOT NULL,         -- single-letter dir code (L/H/O/S/R/G/B/...)
    frame_purpose_id          INTEGER NOT NULL REFERENCES frame_purpose(id),  -- Light vs Stars
    filter_name               TEXT NOT NULL,         -- canonical filter name
    exposure_count            INTEGER NOT NULL,
    total_integration_seconds REAL NOT NULL,
    first_imaged_at           INTEGER NOT NULL,      -- UNIX seconds
    last_imaged_at            INTEGER NOT NULL,
    typical_gain              INTEGER NOT NULL,
    typical_offset            INTEGER NOT NULL,
    typical_set_temp_c        REAL NOT NULL,
    typical_binning_x         INTEGER NOT NULL,
    typical_binning_y         INTEGER NOT NULL,
    typical_exposure_seconds  REAL NOT NULL,
    cameras                   TEXT NOT NULL,         -- CSV of distinct INSTRUME values
    PRIMARY KEY (directory_name, filter_code, frame_purpose_id)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS ix_inventory_filter_dir ON inventory_filter(directory_name);
CREATE INDEX IF NOT EXISTS ix_inventory_filter_name ON inventory_filter(filter_name, frame_purpose_id);
