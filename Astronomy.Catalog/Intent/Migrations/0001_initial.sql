-- 0001_initial.sql — baseline schema of the authored intent store.
-- Applied once through the migration framework (IntentMigrations): unlike the derived catalog's
-- ensure-on-every-open schema.sql, this store is MIGRATED, never rebuilt — the schema evolves only
-- via numbered scripts, each recorded in schema_migration with PRAGMA user_version kept in sync.
-- Rules (the adopted R-set): GUID BLOB(16) big-endian PKs, snake_case singular names, NULL means
-- unset (sentinels forbidden), enum lookups WITH companion CHECKs, every FK indexed (ix_<table>_<col>),
-- booleans INTEGER CHECK (0,1), timestamps UNIX seconds UTC. Epoch ints are the library's canonical
-- ordering (B1950=0, JNow=1, J2000=2) — foreign codes are translated at the boundary, never cast.

-- ----- Enum lookups ---------------------------------------------------------

CREATE TABLE project_state (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT INTO project_state (id, name) VALUES (0, 'Draft'), (1, 'Active'), (2, 'Inactive'), (3, 'Closed');

CREATE TABLE project_priority (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT INTO project_priority (id, name) VALUES (0, 'Low'), (1, 'Normal'), (2, 'High');

CREATE TABLE epoch (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT INTO epoch (id, name) VALUES (0, 'B1950'), (1, 'JNow'), (2, 'J2000');

CREATE TABLE twilight_level (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT INTO twilight_level (id, name) VALUES (0, 'Nighttime'), (1, 'Astronomical'), (2, 'Nautical'), (3, 'Civil');

CREATE TABLE plan_state (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT INTO plan_state (id, name) VALUES (0, 'Draft'), (1, 'Blessed'), (2, 'Superseded');

CREATE TABLE plan_authorship (id INTEGER PRIMARY KEY, name TEXT NOT NULL UNIQUE) WITHOUT ROWID;
INSERT INTO plan_authorship (id, name) VALUES (0, 'Manual'), (1, 'Solver');

-- ----- Intent plane ---------------------------------------------------------

CREATE TABLE profile (
    id                BLOB NOT NULL PRIMARY KEY,
    name              TEXT NOT NULL,
    nina_profile_guid TEXT,             -- correlation to the imaging suite's profile; NULL when unlinked
    created_at        INTEGER NOT NULL
) WITHOUT ROWID;

CREATE TABLE project (
    id                      BLOB NOT NULL PRIMARY KEY,
    profile_id              BLOB NOT NULL REFERENCES profile(id),
    name                    TEXT NOT NULL,
    description             TEXT,
    state_id                INTEGER NOT NULL REFERENCES project_state(id) CHECK (state_id IN (0, 1, 2, 3)),
    priority_id             INTEGER NOT NULL REFERENCES project_priority(id) CHECK (priority_id IN (0, 1, 2)),
    minimum_time_minutes    INTEGER NOT NULL,
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
CREATE INDEX ix_project_profile_id ON project(profile_id);
CREATE INDEX ix_project_state_id ON project(state_id);
CREATE INDEX ix_project_priority_id ON project(priority_id);
CREATE INDEX ix_project_profile_state ON project(profile_id, state_id);

-- Coordinates are authored truth (re-authored from disk centroids at promotion time). They are
-- nullable for exactly one shape: a mosaic PARENT row — the grouping node over its panel children
-- (parent_target_id set on each child; ON DELETE CASCADE) — which carries no coordinates and no
-- exposure plans of its own. Every leaf target carries authored coordinates.
CREATE TABLE target (
    id                    BLOB NOT NULL PRIMARY KEY,
    project_id            BLOB NOT NULL REFERENCES project(id),
    parent_target_id      BLOB REFERENCES target(id) ON DELETE CASCADE,
    name                  TEXT NOT NULL,
    enabled               INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    ra_hours              REAL CHECK (ra_hours IS NULL OR (ra_hours >= 0.0 AND ra_hours < 24.0)),
    dec_degrees_signed    REAL CHECK (dec_degrees_signed IS NULL OR (dec_degrees_signed >= -90.0 AND dec_degrees_signed <= 90.0)),
    epoch_id              INTEGER NOT NULL DEFAULT 2 REFERENCES epoch(id) CHECK (epoch_id IN (0, 1, 2)),
    rotation_deg          REAL,
    priority_id           INTEGER REFERENCES project_priority(id) CHECK (priority_id IS NULL OR priority_id IN (0, 1, 2)),  -- NULL = inherit project
    created_at            INTEGER NOT NULL,
    imported_from_ts_guid TEXT
) WITHOUT ROWID;
CREATE INDEX ix_target_project_id ON target(project_id);
CREATE INDEX ix_target_parent_target_id ON target(parent_target_id);
CREATE INDEX ix_target_epoch_id ON target(epoch_id);
CREATE INDEX ix_target_priority_id ON target(priority_id);

CREATE TABLE exposure_template (
    id                            BLOB NOT NULL PRIMARY KEY,
    profile_id                    BLOB NOT NULL REFERENCES profile(id),
    name                          TEXT NOT NULL,
    filter_name                   TEXT NOT NULL,
    gain                          INTEGER,
    offset_adu                    INTEGER,
    binning                       INTEGER NOT NULL,
    readout_mode                  INTEGER,           -- NULL = camera default
    default_exposure_seconds      REAL NOT NULL,
    twilight_level_id             INTEGER NOT NULL REFERENCES twilight_level(id) CHECK (twilight_level_id IN (0, 1, 2, 3)),
    moon_avoidance_enabled        INTEGER NOT NULL DEFAULT 0 CHECK (moon_avoidance_enabled IN (0, 1)),
    moon_avoidance_separation_deg REAL,
    moon_avoidance_width_days     INTEGER,
    moon_relax_scale              REAL,
    moon_relax_max_altitude_deg   REAL,
    moon_relax_min_altitude_deg   REAL,
    imported_from_ts_guid         TEXT
) WITHOUT ROWID;
CREATE INDEX ix_exposure_template_profile_id ON exposure_template(profile_id);
CREATE INDEX ix_exposure_template_twilight_level_id ON exposure_template(twilight_level_id);

-- Desired counts only — deliberately NO acquired/accepted columns: progress truth is the image
-- library on disk, scanned fresh by the caller; the store never caches actuals.
CREATE TABLE exposure_plan (
    id                    BLOB NOT NULL PRIMARY KEY,
    target_id             BLOB NOT NULL REFERENCES target(id) ON DELETE CASCADE,
    exposure_template_id  BLOB NOT NULL REFERENCES exposure_template(id),
    exposure_seconds      REAL,          -- NULL = inherit the template default
    desired_count         INTEGER NOT NULL DEFAULT 0,
    enabled               INTEGER NOT NULL DEFAULT 1 CHECK (enabled IN (0, 1)),
    imported_from_ts_guid TEXT
) WITHOUT ROWID;
CREATE INDEX ix_exposure_plan_target_id ON exposure_plan(target_id);
CREATE INDEX ix_exposure_plan_exposure_template_id ON exposure_plan(exposure_template_id);

-- ----- Plan plane (minimal — solver arrival adds columns via additive migrations) ----

CREATE TABLE plan (
    id                 BLOB NOT NULL PRIMARY KEY,
    profile_id         BLOB NOT NULL REFERENCES profile(id),
    night_of           TEXT NOT NULL,   -- ISO-8601 local date (yyyy-MM-dd) the plan is for
    state_id           INTEGER NOT NULL DEFAULT 0 REFERENCES plan_state(id) CHECK (state_id IN (0, 1, 2)),
    authored_by_id     INTEGER NOT NULL REFERENCES plan_authorship(id) CHECK (authored_by_id IN (0, 1)),
    switch_immediately INTEGER NOT NULL DEFAULT 0 CHECK (switch_immediately IN (0, 1)),
    created_at         INTEGER NOT NULL,
    blessed_at         INTEGER
) WITHOUT ROWID;
CREATE INDEX ix_plan_profile_id ON plan(profile_id);
CREATE INDEX ix_plan_state_id ON plan(state_id);
CREATE INDEX ix_plan_authored_by_id ON plan(authored_by_id);
CREATE INDEX ix_plan_profile_night ON plan(profile_id, night_of);

CREATE TABLE plan_interval (
    id               BLOB NOT NULL PRIMARY KEY,
    plan_id          BLOB NOT NULL REFERENCES plan(id) ON DELETE CASCADE,
    sequence_number  INTEGER NOT NULL,
    target_id        BLOB NOT NULL REFERENCES target(id),
    exposure_plan_id BLOB NOT NULL REFERENCES exposure_plan(id),
    start_at         INTEGER NOT NULL,
    end_at           INTEGER NOT NULL CHECK (end_at > start_at),   -- authored ends carry the half-exposure margin in stage 0
    amended_by_user  INTEGER NOT NULL DEFAULT 0 CHECK (amended_by_user IN (0, 1)),
    UNIQUE (plan_id, sequence_number)
) WITHOUT ROWID;
CREATE INDEX ix_plan_interval_plan_id ON plan_interval(plan_id);
CREATE INDEX ix_plan_interval_target_id ON plan_interval(target_id);
CREATE INDEX ix_plan_interval_exposure_plan_id ON plan_interval(exposure_plan_id);
