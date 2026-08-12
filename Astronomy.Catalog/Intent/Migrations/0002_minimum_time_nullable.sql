-- 0002_minimum_time_nullable.sql — project.minimum_time_minutes NOT NULL -> nullable.
-- NULL = no minimum (COALESCE-at-read like the other settings columns); an invented 0 would be an
-- R3 sentinel. SQLite cannot ALTER a column's constraints, so this is the R10 table-rebuild: new
-- table, explicit-column copy, drop, rename, recreate the indexes — transactional like every
-- migration, with the framework's whole-store foreign_key_check gate before commit (the framework
-- suspends FK enforcement around the script; PRAGMA foreign_keys is a no-op inside a transaction).
-- The LIVE table is never renamed — renaming it would rewrite child tables' REFERENCES clauses;
-- only the throwaway rebuild name is renamed, which nothing references.

CREATE TABLE project_rebuild (
    id                      BLOB NOT NULL PRIMARY KEY,
    profile_id              BLOB NOT NULL REFERENCES profile(id),
    name                    TEXT NOT NULL,
    description             TEXT,
    state_id                INTEGER NOT NULL REFERENCES project_state(id) CHECK (state_id IN (0, 1, 2, 3)),
    priority_id             INTEGER NOT NULL REFERENCES project_priority(id) CHECK (priority_id IN (0, 1, 2)),
    minimum_time_minutes    INTEGER,            -- NULL = no minimum (was NOT NULL in 0001)
    minimum_altitude_deg    REAL,
    maximum_altitude_deg    REAL,
    use_custom_horizon      INTEGER NOT NULL DEFAULT 0 CHECK (use_custom_horizon IN (0, 1)),
    horizon_offset_deg      REAL NOT NULL DEFAULT 0.0,
    meridian_window_minutes INTEGER,
    filter_switch_frequency INTEGER,
    dither_every            INTEGER,
    smart_exposure_order    INTEGER NOT NULL DEFAULT 0 CHECK (smart_exposure_order IN (0, 1)),
    is_mosaic               INTEGER NOT NULL DEFAULT 0 CHECK (is_mosaic IN (0, 1)),
    created_at              INTEGER NOT NULL,
    active_at               INTEGER,
    inactive_at             INTEGER,
    imported_from_ts_guid   TEXT
) WITHOUT ROWID;

INSERT INTO project_rebuild (id, profile_id, name, description, state_id, priority_id,
    minimum_time_minutes, minimum_altitude_deg, maximum_altitude_deg, use_custom_horizon,
    horizon_offset_deg, meridian_window_minutes, filter_switch_frequency, dither_every,
    smart_exposure_order, is_mosaic, created_at, active_at, inactive_at, imported_from_ts_guid)
SELECT id, profile_id, name, description, state_id, priority_id,
    minimum_time_minutes, minimum_altitude_deg, maximum_altitude_deg, use_custom_horizon,
    horizon_offset_deg, meridian_window_minutes, filter_switch_frequency, dither_every,
    smart_exposure_order, is_mosaic, created_at, active_at, inactive_at, imported_from_ts_guid
FROM project;

DROP TABLE project;
ALTER TABLE project_rebuild RENAME TO project;

CREATE INDEX ix_project_profile_id ON project(profile_id);
CREATE INDEX ix_project_state_id ON project(state_id);
CREATE INDEX ix_project_priority_id ON project(priority_id);
CREATE INDEX ix_project_profile_state ON project(profile_id, state_id);
