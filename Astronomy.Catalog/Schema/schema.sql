-- schema.sql — full Catalog.db schema.
-- No migration framework: the catalog is fully DERIVED (disk scan = ACTUAL, TS import = PLANNED, reconciled by
-- CatalogBuilder), so it is always rebuildable. Applied idempotently on open (CREATE TABLE IF NOT EXISTS +
-- INSERT OR IGNORE); a schema change just means deleting the regenerable Catalog.db. WAL / foreign_keys /
-- synchronous are set by SchemaManager on open. Rules: GUID BLOB(16) PKs, snake_case, NULL-not-sentinel,
-- enum lookup tables, indexed FKs.
--
-- Source-of-truth model: the disk library (E:\Photography\Astro Photography\Processing) is ACTUAL; TS is the
-- PLAN, maintained against actual. The catalog re-organizes the plan clean and anchors it to actual: ONE
-- canonical `target` carries both facets (disk identity + plan attributes), distinguished by `source_id`.
-- `inventory_filter` (what was shot) and `exposure_plan` (what was planned) both hang off that one target.

-- ----- Lookup tables --------------------------------------------------------

CREATE TABLE IF NOT EXISTS project_state (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO project_state (id, name) VALUES (0, 'Draft'), (1, 'Active'), (2, 'Inactive'), (3, 'Closed');

CREATE TABLE IF NOT EXISTS project_priority (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO project_priority (id, name) VALUES (0, 'Low'), (1, 'Normal'), (2, 'High');

CREATE TABLE IF NOT EXISTS epoch (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO epoch (id, name) VALUES (0, 'B1950'), (1, 'JNow'), (2, 'J2000');

CREATE TABLE IF NOT EXISTS frame_purpose (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO frame_purpose (id, name) VALUES (0, 'Light'), (1, 'Stars');

-- How a target entered the catalog: shot on disk only, planned in TS only, or both (planned AND shot).
CREATE TABLE IF NOT EXISTS target_source (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT OR IGNORE INTO target_source (id, name) VALUES (0, 'Actual'), (1, 'Planned'), (2, 'Both');

-- ----- Profiles & projects (plan plane, imported from TS) -------------------

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

-- ----- Canonical target (disk identity + plan attributes, one row per object) ----
-- `source_id` discriminates the three provenance cases:
--   Actual  (0): on disk, no TS match     -> directory_name set, project_id NULL, imported_from_ts_guid NULL
--   Planned (1): in TS, not yet on disk    -> directory_name NULL, project_id set, imported_from_ts_guid set
--   Both    (2): planned AND shot (merged) -> directory_name set, project_id set, imported_from_ts_guid set
-- When merged, disk coordinates win (plate-solved = truth) and imported_from_ts_guid is retained so a future
-- TargetSchedulerWriter can map catalog edits back to the exact TS target row.
-- A mosaic is one PARENT row (directory_name = the mosaic directory; carries no plans/inventory) plus one
-- CHILD row per panel (parent_target_id set; directory_name = '<mosaic dir>/<panel label>', satisfying the
-- UNIQUE constraint). Plans and inventory hang off the children, each with its own provenance and coordinates.

CREATE TABLE IF NOT EXISTS target (
    id                    BLOB NOT NULL PRIMARY KEY,
    source_id             INTEGER NOT NULL REFERENCES target_source(id),
    project_id            BLOB REFERENCES project(id),                  -- NULL for actual-only
    parent_target_id      BLOB REFERENCES target(id) ON DELETE CASCADE, -- set on a panel child; NULL top-level
    name                  TEXT NOT NULL,                                -- canonical (disk directory name when on disk)
    enabled               INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    ra_hours              REAL CHECK (ra_hours IS NULL OR (ra_hours >= 0.0 AND ra_hours < 24.0)),
    dec_degrees_signed    REAL CHECK (dec_degrees_signed IS NULL OR (dec_degrees_signed >= -90.0 AND dec_degrees_signed <= 90.0)),
    epoch_id              INTEGER NOT NULL DEFAULT 2 REFERENCES epoch(id),
    rotation_deg          REAL,
    roi_percent           REAL,
    priority_id           INTEGER REFERENCES project_priority(id),
    directory_name        TEXT UNIQUE,                                  -- disk identity; NULL for planned-only
    catalog               TEXT,                                         -- before " - " (NULL for planned-only)
    common_name           TEXT,                                         -- after " - "
    object_name           TEXT,                                         -- FITS OBJECT consensus
    scanned_at            INTEGER,                                      -- UNIX seconds of the scan (NULL planned-only)
    created_at            INTEGER NOT NULL,
    imported_from_ts_guid TEXT                                          -- TS target guid (retained when merged)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS ix_target_project_id ON target(project_id);
CREATE INDEX IF NOT EXISTS ix_target_source ON target(source_id);
CREATE INDEX IF NOT EXISTS ix_target_parent ON target(parent_target_id);

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

-- ----- Goals: exposure_plan (PLANNED, per target/filter) --------------------

CREATE TABLE IF NOT EXISTS exposure_plan (
    id                    BLOB NOT NULL PRIMARY KEY,
    target_id             BLOB NOT NULL REFERENCES target(id) ON DELETE CASCADE,
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

-- ----- Actuals: inventory_filter (what was shot, per target/filter/purpose/exposure) --
-- Derived ImageLibraryScanner aggregates; keyed to the canonical target (only Actual/Both targets have rows).

CREATE TABLE IF NOT EXISTS inventory_filter (
    target_id                 BLOB NOT NULL REFERENCES target(id) ON DELETE CASCADE,
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
    exposure_seconds          REAL NOT NULL,         -- whole-second sub-length bucket; part of the row identity
    cameras                   TEXT NOT NULL,         -- CSV of distinct INSTRUME values
    PRIMARY KEY (target_id, filter_code, frame_purpose_id, exposure_seconds)
) WITHOUT ROWID;
CREATE INDEX IF NOT EXISTS ix_inventory_filter_target ON inventory_filter(target_id);
CREATE INDEX IF NOT EXISTS ix_inventory_filter_name ON inventory_filter(filter_name, frame_purpose_id);
