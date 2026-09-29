-- CORE canonical snapshot (.core), schema version 1.
--
-- One file holds one immutable snapshot (ADR-009). Coordinates are REAL (64-bit)
-- in project feet unless a column says otherwise (ADR-010). JSON columns hold
-- source-faithful detail that is not queried relationally.
--
-- Requires SQLite 3.37+ (STRICT tables) with the R*Tree module.

PRAGMA foreign_keys = ON;

CREATE TABLE meta (
  key   TEXT PRIMARY KEY,
  value TEXT NOT NULL
) STRICT;
-- Required keys: schema_version ('1'), snapshot_id.

CREATE TABLE project (
  id   TEXT PRIMARY KEY,
  name TEXT NOT NULL
) STRICT;

CREATE TABLE snapshot (
  id                      TEXT PRIMARY KEY,
  project_id              TEXT NOT NULL REFERENCES project(id),
  corex_sha256            TEXT NOT NULL,
  corex_version           TEXT NOT NULL,
  exporter_name           TEXT NOT NULL,
  exporter_version        TEXT NOT NULL,
  host_product            TEXT NOT NULL,
  host_version            TEXT NOT NULL,
  importer_version        TEXT NOT NULL,
  mapping_profile_sha256  TEXT NOT NULL,
  ai_enabled              INTEGER NOT NULL CHECK (ai_enabled IN (0, 1)),
  ai_model                TEXT,
  created_at              TEXT NOT NULL,
  CHECK (ai_enabled = 1 OR ai_model IS NULL)
) STRICT;

-- Sources -------------------------------------------------------------------

CREATE TABLE source_file (
  id           INTEGER PRIMARY KEY,
  kind         TEXT NOT NULL CHECK (kind IN ('dwg', 'dst', 'shortcut', 'pdf', 'other')),
  path         TEXT NOT NULL,
  sha256       TEXT NOT NULL,
  size_bytes   INTEGER
) STRICT;

CREATE TABLE drawing (
  id                     INTEGER PRIMARY KEY,
  source_file_id         INTEGER NOT NULL UNIQUE REFERENCES source_file(id),
  corex_id               TEXT NOT NULL UNIQUE,
  fingerprint_guid       TEXT NOT NULL,
  version_guid           TEXT NOT NULL,
  insunits               INTEGER NOT NULL,
  linear_units           TEXT NOT NULL,
  to_project_units       REAL NOT NULL,
  coordinate_system_code TEXT,
  drawing_scale          REAL,
  grid_to_ground_scale   REAL,
  visretain              INTEGER NOT NULL CHECK (visretain IN (0, 1))
) STRICT;
-- fingerprint_guid is not UNIQUE: copied drawings share it and are reported by validation.

CREATE TABLE layer (
  id             INTEGER PRIMARY KEY,
  drawing_id     INTEGER NOT NULL REFERENCES drawing(id),
  name           TEXT NOT NULL,
  is_on          INTEGER NOT NULL CHECK (is_on IN (0, 1)),
  is_frozen      INTEGER NOT NULL CHECK (is_frozen IN (0, 1)),
  is_locked      INTEGER NOT NULL CHECK (is_locked IN (0, 1)),
  is_plot        INTEGER NOT NULL CHECK (is_plot IN (0, 1)),
  dependent_xref TEXT,
  UNIQUE (drawing_id, name)
) STRICT;

CREATE TABLE block_definition (
  id         INTEGER PRIMARY KEY,
  drawing_id INTEGER NOT NULL REFERENCES drawing(id),
  handle     TEXT NOT NULL,
  name       TEXT NOT NULL,
  kind       TEXT NOT NULL CHECK (kind IN ('block', 'xref', 'anonymous')),
  origin_x   REAL NOT NULL,
  origin_y   REAL NOT NULL,
  origin_z   REAL NOT NULL,
  UNIQUE (drawing_id, handle)
) STRICT;

CREATE TABLE style (
  id          INTEGER PRIMARY KEY,
  drawing_id  INTEGER NOT NULL REFERENCES drawing(id),
  handle      TEXT NOT NULL,
  name        TEXT NOT NULL,
  kind        TEXT NOT NULL CHECK (kind IN ('object', 'label')),
  object_type TEXT NOT NULL,
  UNIQUE (drawing_id, handle)
) STRICT;

CREATE TABLE display_component (
  id         INTEGER PRIMARY KEY,
  style_id   INTEGER NOT NULL REFERENCES style(id),
  name       TEXT NOT NULL,
  view       TEXT NOT NULL CHECK (view IN ('plan', 'model', 'profile', 'section')),
  is_visible INTEGER NOT NULL CHECK (is_visible IN (0, 1)),
  layer_name TEXT NOT NULL
) STRICT;

CREATE TABLE geometry (
  id           INTEGER PRIMARY KEY,
  kind         TEXT NOT NULL,
  space        TEXT NOT NULL CHECK (space IN ('ocs', 'block', 'source', 'world', 'paper', 'profile-view')),
  units        TEXT NOT NULL,
  is_derived   INTEGER NOT NULL CHECK (is_derived IN (0, 1)),
  data_json    TEXT,
  data_blob    BLOB,
  tolerance    REAL,
  derivation   TEXT,
  CHECK (data_json IS NOT NULL OR data_blob IS NOT NULL),
  CHECK (is_derived = 0 OR derivation IS NOT NULL)
) STRICT;

-- Coarse index only: R*Tree stores 32-bit floats; checks re-test exact geometry (ADR-010).
CREATE VIRTUAL TABLE geometry_rtree USING rtree(
  id,
  min_x, max_x,
  min_y, max_y
);

CREATE TABLE source_entity (
  id               INTEGER PRIMARY KEY,
  drawing_id       INTEGER NOT NULL REFERENCES drawing(id),
  handle           TEXT NOT NULL,
  kind             TEXT NOT NULL,
  class            TEXT NOT NULL,
  name             TEXT,
  space            TEXT NOT NULL CHECK (space IN ('model', 'paper', 'block')),
  owner_handle     TEXT,
  layer_name       TEXT NOT NULL,
  is_visible       INTEGER NOT NULL CHECK (is_visible IN (0, 1)),
  is_proxy         INTEGER NOT NULL CHECK (is_proxy IN (0, 1)),
  style_id         INTEGER REFERENCES style(id),
  geometry_id      INTEGER REFERENCES geometry(id),
  payload_json     TEXT,
  properties_json  TEXT,
  attached_json    TEXT,
  UNIQUE (drawing_id, handle),
  CHECK (space = 'model' OR owner_handle IS NOT NULL)
) STRICT;
-- payload_json holds the typed COREX payload (pipe, structure, text, ...) verbatim.

CREATE TABLE block_reference (
  source_entity_id    INTEGER PRIMARY KEY REFERENCES source_entity(id),
  block_definition_id INTEGER NOT NULL REFERENCES block_definition(id),
  transform_json      TEXT NOT NULL
) STRICT;

CREATE TABLE attribute (
  id               INTEGER PRIMARY KEY,
  source_entity_id INTEGER NOT NULL REFERENCES source_entity(id),
  tag              TEXT NOT NULL,
  value            TEXT NOT NULL
) STRICT;

CREATE TABLE xref_instance (
  id                  INTEGER PRIMARY KEY,
  host_drawing_id     INTEGER NOT NULL REFERENCES drawing(id),
  handle              TEXT NOT NULL,
  block_name          TEXT NOT NULL,
  target_drawing_id   INTEGER REFERENCES drawing(id),
  attachment          TEXT NOT NULL CHECK (attachment IN ('attach', 'overlay')),
  saved_path          TEXT NOT NULL,
  resolved_path       TEXT,
  resolved_by         TEXT NOT NULL,
  status              TEXT NOT NULL CHECK (status IN ('resolved', 'unloaded', 'unreferenced', 'not-found', 'unresolved')),
  layer_name          TEXT NOT NULL,
  unit_scale          REAL NOT NULL,
  transform_json      TEXT NOT NULL,
  clip_json           TEXT,
  UNIQUE (host_drawing_id, handle),
  CHECK (status <> 'resolved' OR target_drawing_id IS NOT NULL)
) STRICT;

-- Resolved Xref chains from each root drawing; one row per path.
CREATE TABLE xref_path (
  id                    INTEGER PRIMARY KEY,
  root_drawing_id       INTEGER NOT NULL REFERENCES drawing(id),
  leaf_drawing_id       INTEGER REFERENCES drawing(id),
  instance_ids_json     TEXT NOT NULL,
  is_loaded             INTEGER NOT NULL CHECK (is_loaded IN (0, 1)),
  not_loaded_reason     TEXT,
  world_transform_json  TEXT,
  CHECK (is_loaded = 1 OR not_loaded_reason IS NOT NULL)
) STRICT;

CREATE TABLE data_reference (
  id                   INTEGER PRIMARY KEY,
  reference_entity_id  INTEGER NOT NULL UNIQUE REFERENCES source_entity(id),
  source_entity_id     INTEGER REFERENCES source_entity(id),
  shortcut_name        TEXT NOT NULL,
  status               TEXT NOT NULL CHECK (status IN ('current', 'out-of-date', 'source-not-found', 'object-not-found'))
) STRICT;

CREATE TABLE shortcut (
  id                 INTEGER PRIMARY KEY,
  name               TEXT NOT NULL,
  object_type        TEXT NOT NULL,
  source_path        TEXT NOT NULL,
  source_drawing_id  INTEGER REFERENCES drawing(id),
  source_handle      TEXT,
  status             TEXT NOT NULL CHECK (status IN ('valid', 'source-not-found', 'object-not-found'))
) STRICT;

-- Design Model ----------------------------------------------------------------

CREATE TABLE design_object (
  id              INTEGER PRIMARY KEY,
  stable_key      TEXT NOT NULL UNIQUE,
  type            TEXT NOT NULL,
  name            TEXT,
  system          TEXT,
  basis           TEXT NOT NULL CHECK (basis IN ('native', 'rule', 'ai')),
  properties_json TEXT NOT NULL,
  geometry_id     INTEGER REFERENCES geometry(id)
) STRICT;
-- stable_key: type plus the sorted stable identities (fingerprint:handle) of its source mappings (ADR-009).
-- basis: least-certain method among the mappings and classifications that define the object.

CREATE TABLE entity_mapping (
  id               INTEGER PRIMARY KEY,
  design_object_id INTEGER NOT NULL REFERENCES design_object(id),
  source_entity_id INTEGER NOT NULL REFERENCES source_entity(id),
  method           TEXT NOT NULL CHECK (method IN ('native', 'rule', 'ai')),
  via              TEXT NOT NULL CHECK (via IN ('direct', 'data-reference')),
  rule_ref         TEXT,
  confidence       REAL,
  evidence_json    TEXT,
  UNIQUE (design_object_id, source_entity_id),
  CHECK (method <> 'rule' OR rule_ref IS NOT NULL),
  CHECK (method <> 'ai' OR confidence IS NOT NULL)
) STRICT;

CREATE TABLE classification (
  id               INTEGER PRIMARY KEY,
  design_object_id INTEGER NOT NULL REFERENCES design_object(id),
  property         TEXT NOT NULL,
  value            TEXT NOT NULL,
  method           TEXT NOT NULL CHECK (method IN ('native', 'rule', 'ai')),
  rule_ref         TEXT,
  confidence       REAL,
  UNIQUE (design_object_id, property)
) STRICT;

CREATE TABLE design_relationship (
  id      INTEGER PRIMARY KEY,
  kind    TEXT NOT NULL,
  from_id INTEGER NOT NULL REFERENCES design_object(id),
  to_id   INTEGER NOT NULL REFERENCES design_object(id),
  role    TEXT,
  method  TEXT NOT NULL CHECK (method IN ('native', 'rule', 'ai'))
) STRICT;
-- Examples: kind 'pipe-structure' with role 'start' or 'end'; kind 'profile-alignment'.

-- Document Model --------------------------------------------------------------

CREATE TABLE layout (
  id              INTEGER PRIMARY KEY,
  drawing_id      INTEGER NOT NULL REFERENCES drawing(id),
  handle          TEXT NOT NULL,
  name            TEXT NOT NULL,
  tab_order       INTEGER NOT NULL,
  title_block_entity_id INTEGER REFERENCES source_entity(id),
  UNIQUE (drawing_id, handle)
) STRICT;

CREATE TABLE viewport (
  id                    INTEGER PRIMARY KEY,
  layout_id             INTEGER NOT NULL REFERENCES layout(id),
  handle                TEXT NOT NULL,
  number                INTEGER NOT NULL,
  is_paper_space_view   INTEGER NOT NULL CHECK (is_paper_space_view IN (0, 1)),
  kind                  TEXT CHECK (kind IN ('plan', 'profile')),
  is_on                 INTEGER NOT NULL CHECK (is_on IN (0, 1)),
  center_paper_x        REAL NOT NULL,
  center_paper_y        REAL NOT NULL,
  width_paper           REAL NOT NULL,
  height_paper          REAL NOT NULL,
  view_json             TEXT,
  clip_json             TEXT,
  footprint_geometry_id INTEGER REFERENCES geometry(id),
  UNIQUE (layout_id, handle),
  CHECK (is_paper_space_view = 1 OR (kind IS NOT NULL AND view_json IS NOT NULL))
) STRICT;

CREATE TABLE viewport_frozen_layer (
  viewport_id INTEGER NOT NULL REFERENCES viewport(id),
  layer_name  TEXT NOT NULL,
  PRIMARY KEY (viewport_id, layer_name)
) STRICT;

CREATE TABLE profile_view (
  id                   INTEGER PRIMARY KEY,
  source_entity_id     INTEGER NOT NULL UNIQUE REFERENCES source_entity(id),
  alignment_object_id  INTEGER REFERENCES design_object(id),
  station_start        REAL NOT NULL,
  station_end          REAL NOT NULL,
  elevation_min        REAL NOT NULL,
  elevation_max        REAL NOT NULL,
  vertical_exaggeration REAL NOT NULL,
  origin_x             REAL NOT NULL,
  origin_y             REAL NOT NULL
) STRICT;

CREATE TABLE sheet (
  id        INTEGER PRIMARY KEY,
  number    TEXT NOT NULL UNIQUE,
  title     TEXT NOT NULL,
  revision  TEXT,
  layout_id INTEGER NOT NULL UNIQUE REFERENCES layout(id),
  source    TEXT NOT NULL CHECK (source IN ('sheet-set', 'title-block'))
) STRICT;

CREATE TABLE document (
  id             INTEGER PRIMARY KEY,
  source_file_id INTEGER NOT NULL REFERENCES source_file(id),
  kind           TEXT NOT NULL CHECK (kind IN ('pdf-set', 'report', 'calculation', 'specification', 'standard', 'other')),
  title          TEXT
) STRICT;

CREATE TABLE sheet_rendition (
  id          INTEGER PRIMARY KEY,
  sheet_id    INTEGER NOT NULL REFERENCES sheet(id),
  document_id INTEGER NOT NULL REFERENCES document(id),
  page        INTEGER NOT NULL,
  UNIQUE (document_id, page)
) STRICT;

CREATE TABLE annotation (
  id                INTEGER PRIMARY KEY,
  source_entity_id  INTEGER NOT NULL UNIQUE REFERENCES source_entity(id),
  kind              TEXT NOT NULL CHECK (kind IN ('label', 'mleader', 'mtext', 'text', 'table', 'dimension')),
  plain_text        TEXT,
  text_overridden   INTEGER CHECK (text_overridden IN (0, 1)),
  parsed_json       TEXT,
  design_object_id  INTEGER REFERENCES design_object(id),
  link_method       TEXT,
  link_distance     REAL,
  CHECK (design_object_id IS NULL OR link_method IS NOT NULL)
) STRICT;

CREATE TABLE coverage_intent (
  id                   INTEGER PRIMARY KEY,
  source_entity_id     INTEGER NOT NULL UNIQUE REFERENCES source_entity(id),
  sheet_id             INTEGER REFERENCES sheet(id),
  boundary_geometry_id INTEGER NOT NULL REFERENCES geometry(id)
) STRICT;

CREATE TABLE presentation_instance (
  id               INTEGER PRIMARY KEY,
  viewport_id      INTEGER NOT NULL REFERENCES viewport(id),
  source_entity_id INTEGER NOT NULL REFERENCES source_entity(id),
  xref_path_id     INTEGER REFERENCES xref_path(id),
  design_object_id INTEGER REFERENCES design_object(id),
  is_inside        INTEGER NOT NULL CHECK (is_inside IN (0, 1)),
  is_visible       INTEGER NOT NULL CHECK (is_visible IN (0, 1)),
  reasons_json     TEXT NOT NULL DEFAULT '[]',
  effective_layer  TEXT,
  CHECK (is_visible = 0 OR is_inside = 1)
) STRICT;
-- xref_path_id is NULL for entities native to the viewport's drawing.
-- Rows are written only for entities inside the footprint (is_inside = 1) in the first build.

-- Validation ------------------------------------------------------------------

CREATE TABLE validation_message (
  id           INTEGER PRIMARY KEY,
  severity     TEXT NOT NULL CHECK (severity IN ('error', 'warning')),
  code         TEXT NOT NULL,
  message      TEXT NOT NULL,
  subject_json TEXT
) STRICT;

-- Indexes -----------------------------------------------------------------------

CREATE INDEX ix_source_entity_kind ON source_entity(kind);
CREATE INDEX ix_entity_mapping_source ON entity_mapping(source_entity_id);
CREATE INDEX ix_design_object_type ON design_object(type);
CREATE INDEX ix_presentation_viewport ON presentation_instance(viewport_id);
CREATE INDEX ix_presentation_object ON presentation_instance(design_object_id);
CREATE INDEX ix_xref_instance_target ON xref_instance(target_drawing_id);
