# COREX Exchange Format

## Purpose

COREX (`.corex`) is the exchange format between Autodesk Civil 3D and CORE.

It extracts the information CORE needs from Civil 3D without requiring the CORE engine to understand DWG files or depend on Civil 3D.

```text
Civil 3D Project
       |
       v
CORE Civil 3D Exporter
       |
       v
     .corex
       |
       v
CORE Importer / Normalizer
       |
       v
      .core
```

- **`.corex`** is raw, faithful exchange data extracted from Civil 3D.
- **`.core`** is CORE's canonical, interpreted project model.

## Design principle

CORE will not implement its own DWG parser. The Civil 3D exporter uses the official Autodesk AutoCAD and Civil 3D .NET APIs to access the native drawing database and Civil 3D objects.

The exporter remains deliberately simple: it extracts what Civil 3D knows and preserves where it came from. It does not perform engineering QC or make unnecessary engineering interpretations.

## Export pipeline

### 1. Project discovery

The exporter identifies the drawings that make up the project, including sheet, design, base, survey, Xref, and nested-Xref drawings. It follows three kinds of dependency and preserves each rather than flattening it:

- **Xrefs** — attached and overlaid external references, including nested Xrefs.
- **Data shortcuts** — Civil 3D data references to alignments, profiles, surfaces, pipe networks, pressure networks, and other objects published from a source drawing through the project's `_Shortcuts` folder. See [ADR-008](adr/ADR-008-data-shortcuts.md).
- **Sheet set** — the `.dst` sheet set, its sheets, and the layouts they point to, when one exists.

```text
C301.dwg
|
|-- xref   C-BASE.dwg
|   `-- xref   SURVEY.dwg
|
|-- xref   C-GRAD.dwg
|-- xref   C-STRM.dwg
|-- xref   C-UTIL.dwg
|
|-- dref   Alignment "SW-A"      -> C-ALIGN.dwg
|-- dref   Profile   "SW-A FG"   -> C-ALIGN.dwg
`-- dref   Surface   "FG"        -> C-GRAD.dwg
```

For every dependency, COREX records the saved path, the resolved path, how it was resolved (relative, absolute, support search path, or project folder), and the resolution status (resolved, unresolved, unloaded, not found). A path that resolves on the author's machine but not the reviewer's must be visible as such.

A drawing that is referenced from several places is extracted once. Each reference is recorded separately.

### 2. Drawing extraction

For every drawing, COREX records the AutoCAD information needed by CORE, including drawing metadata, layers, block definitions and block references (with attributes), lines, polylines, 3D polylines, arcs, circles, text, MText, MLeaders, dimensions, tables, hatches, and Xrefs. Native handles and identifiers are preserved whenever possible.

Drawing metadata includes:

- File path, size, and checksum.
- `Database.FingerprintGuid` (stable identity of a drawing across saves and renames) and `Database.VersionGuid` (changes on every save).
- AutoCAD and Civil 3D version that last saved the file.
- Insertion units (`INSUNITS`), distinguishing US survey feet from international feet.
- Civil 3D drawing settings: coordinate system code, drawing scale, and grid-to-ground scale factor where set.

Geometry rules:

- **Preserve analytic geometry.** Arcs, polyline bulges, circles, and alignment spirals are recorded as curves, not tessellated.
- **Preserve the object coordinate system.** Planar AutoCAD entities such as lightweight polylines, circles, and arcs are stored in their object coordinate system (OCS). COREX records the entity normal and elevation with the native coordinates. Any conversion to world coordinates is recorded as derived.
- **Preserve block nesting.** Entities inside block definitions are recorded once in the definition. Each block reference records its insertion point, scale, rotation, normal, and full transform, including nested references.
- **Use 64-bit floats for coordinates.** State-plane coordinates are in the millions; 32-bit floats lose tenths of a foot at that magnitude.

Entities that the exporter cannot interpret, such as proxy objects from other Autodesk verticals, are still recorded with their native class name, handle, layer, and extents.

### 3. Civil 3D extraction

COREX also records native Civil 3D objects. Initial support focuses on:

- Gravity pipe networks, pipes, and structures, including each pipe's native start and end structure connections.
- Pressure networks, pressure pipes, fittings, and appurtenances. These use a separate Civil 3D API and object model from gravity networks.
- Surfaces, feature lines, alignments, profiles, and profile views.
- Plan-production view frame groups, view frames, and match lines, which record the intended sheet coverage.

Additional object types, such as corridors, sample lines, and section views, can be added later.

The exporter preserves native Civil 3D properties rather than prematurely converting them into CORE engineering concepts. Values are recorded in drawing units with the units stated; for example, a pipe's inner diameter is recorded as `2.0` in drawing units of feet, not converted to `24 in`.

For every Civil 3D object, COREX also records:

- **Reference status** — whether the object is native to the drawing or a data-shortcut reference, and if a reference, its source drawing, source object, and shortcut file.
- **Style** — the object style and label styles in use (see [Display and visibility](#display-and-visibility)).
- **Attached data** — Map 3D Object Data and AutoCAD property sets, when present. These often carry the attributes engineers expect to be complete.

Civil 3D labels are separate objects from the objects they annotate. COREX records each label with its style, the object it annotates, its position, and its displayed text where the API exposes it, including whether the text has been overridden.

## Sheets, layouts, and viewports

Layouts represent what is ultimately presented on construction drawings. COREX records each layout's paper-space entities, title block (including attribute values such as sheet number, title, and revision), notes, tables, and viewports.

The first viewport in each layout's viewport list is the paper-space view itself; COREX marks it so it is never treated as a model-space window.

For every viewport, COREX preserves:

- Paper-space position, dimensions, boundary, and nonrectangular clipping boundary when applicable.
- Model-space view center, target, direction, height, custom scale, twist/rotation, and other values required to reconstruct the viewport transform.
- On/off state and annotation scale.

This allows CORE to relate model/world coordinates to sheet/paper coordinates and determine each viewport's world-coordinate footprint.

### Profile and section views

A viewport that looks at a profile view shows station and elevation, not plan coordinates. For every profile view, COREX records its model-space placement, station and elevation range, vertical exaggeration, and the alignment and profiles it displays, so CORE can map between profile-view space and station/elevation. The Civil 3D API provides this mapping through `ProfileView.FindXYAtStationAndElevation` and `ProfileView.FindStationAndElevationAtXY`. Section views are handled the same way when they are added.

## Display and visibility

Whether an object is *inside* a viewport and whether it is *visible* in that viewport are different questions. COREX records what CORE needs to answer the second one.

### Layer state

Layer visibility is preserved for each viewport, including layer on/off state, global freeze state, viewport freeze state, and relevant viewport-specific overrides. For Xref objects, the applicable host and Xref layer state is also preserved, along with the host's `VISRETAIN` setting.

### Xref and block rules

- **Attach vs. overlay.** Overlaid Xrefs are not visible through a parent drawing. COREX records the attachment type so nested visibility can be determined.
- **Xref clipping.** Xref and block clip boundaries (spatial filters), including inverted clips, are recorded.
- **Layer 0 inheritance.** Entities on layer `0` inside a block definition take on the layer of the block reference. COREX records the definition layer and the reference layer so CORE can resolve the effective layer.

### Civil 3D style display components

Civil 3D object visibility is controlled by styles, not only by the object's own layer. Each style has display components (for example, a pipe's outline and centerline, or a structure's plan symbol). Each component has its own layer, visibility setting, and view direction (plan, model, profile, section). A pipe whose style is "No Display", or whose component layers are frozen in a viewport, is invisible even when the pipe's own layer is on.

For every style in use, COREX records each display component's name, view direction, visibility, and layer. Label styles are recorded the same way.

```text
Design Object: DI-12
Source: C-STRM.dwg
Object layer: C-STRM-STRC
Style: "Curb Inlet - Plan"
  Component "Structure": visible, layer C-STRM-STRC-SYMB

    v Xref

Host layer: C-STRM|C-STRM-STRC-SYMB

    v Viewport VP-1

VP frozen: true

DI-12 is not presented in this viewport.
```

## Xref lineage and transforms

COREX preserves the complete Xref hierarchy and transformation for every Xref instance, including the unit scaling applied when host and Xref insertion units differ.

```text
SURVEY.dwg
     v
C-BASE.dwg
     v
C-SITE.dwg
     v
C301.dwg
```

This makes the full coordinate path available:

```text
Source geometry (OCS)
  -> Block reference transform chain
  -> Xref transform chain
  -> World geometry
  -> Viewport transform (or profile-view mapping)
  -> Sheet geometry
```

Xref lineage must not be flattened during export.

## Package structure

COREX is a portable package, not one enormous JSON file. The `.corex` extension may represent a packaged archive whose internal format can evolve.

```text
SmithFarm.corex
|
|-- manifest.json
|
|-- drawings/
|   |-- C-BASE.json
|   |-- C-GRAD.json
|   |-- C-STRM.json
|   |-- C-UTIL.json
|   |-- C301.json
|   `-- C501.json
|
|-- shortcuts/
|   `-- shortcuts.json
|
|-- sheets/
|   `-- sheets.json
|
`-- geometry/
    |-- EG.*
    |-- FG.*
    `-- ...
```

Styles, layers, block definitions, layouts, and viewports belong to a drawing and are recorded in that drawing's file.

The package is a zip archive (deflate) of this layout. Readers also accept the unpacked directory, which is how test fixtures are stored.

The manifest records the COREX schema version, exporter version, Civil 3D and AutoCAD versions used for export, export time, the drawing list with checksums and fingerprint GUIDs, and the dependency graph.

JSON is appropriate for metadata, object properties, relationships, layers, Xrefs, data shortcuts, layouts, viewports, styles, and Civil 3D attributes. Large geometry, such as TIN surfaces, dense meshes, and large point collections, should use an efficient binary representation referenced by JSON records. Small geometry may be written inline as JSON. Coordinates must be 64-bit floats or stored relative to a recorded local origin.

### Binary geometry (CXB1)

Binary geometry files use the `CXB1` format. All values are little-endian.

| Offset | Type | Field |
| --- | --- | --- |
| 0 | 4 bytes | Magic `CXB1` |
| 4 | uint32 | Format version (`1`) |
| 8 | uint32 | Kind: `1` = TIN, `2` = point cloud, `3` = polyface mesh |
| 12 | uint32 | Flags (reserved, `0`) |
| 16 | float64 × 3 | Local origin; stored coordinates are relative to it |
| 40 | uint64 | Vertex count *V* |
| 48 | uint64 | Face count *F* (`0` for point clouds) |
| 56 | float64 × 3*V* | Vertex coordinates (x, y, z) |
| … | uint32 × 3*F* | Triangle vertex indices (TIN and mesh) |

The referencing JSON record carries the file's SHA-256 so corruption is detected on import.

## Schemas and fixtures

The normative definition of COREX is the JSON Schema set in [`schemas/corex/`](../schemas/corex/). This document explains intent; where it and the schemas disagree, the schemas win and this document is corrected.

A complete example package with expected import and review results is in [`fixtures/corex/mini-site/`](../fixtures/corex/mini-site/).

## COREX is not the canonical model

COREX represents what Civil 3D reported, not CORE's final interpretation of the project.

```text
COREX
Native type: AeccDbPipe
Handle: 2A7F
Name: P-12
Diameter: 2.0 drawing units (ft)

CORE
DesignObject: GravityPipe (system: storm, by rule)
Name: P-12
Diameter: 24 in
```

The CORE importer reads `.corex`, creates SourceEntities, normalizes values and units, creates DesignObjects, builds the Document Model, resolves relationships, validates the result, and saves `project.core`.

DesignObject creation follows this priority:

1. Structured deterministic information
2. Rule-based interpretation
3. AI-assisted interpretation only when necessary

For example, a Civil 3D pipe deterministically becomes a `GravityPipe` DesignObject without AI. Whether that pipe is storm or sanitary is not something Civil 3D records, so the system is classified by an explicit rule (for example, on network name) with a `rule` method.

A data-shortcut reference to a pipe network does not create a second network. The importer maps the reference and the source object to the same DesignObject and records the reference relationship.

## Debugging and development boundary

COREX is the explicit boundary between Civil 3D extraction and CORE normalization. If CORE reports `P-12` as 18 inches but `.corex` records 24 inches, the error is in import or normalization. If `.corex` records 18 inches, the error is in extraction. This makes both systems easier to test.

It also lets the CORE engine be developed and tested without Civil 3D running. A developer can receive `SmithFarm.corex` and test import, normalization, relationships, geometry, viewport mapping, QC rules, UI, and findings without receiving the original DWGs or requiring Autodesk software.

Civil 3D's native LandXML export (surfaces, alignments, profiles, and pipe networks) can be used during development to test the canonical Design Model before the exporter is complete, and later as a cross-check of exported values. LandXML is not a substitute for COREX: it carries no layouts, viewports, Xrefs, data-shortcut lineage, or display state.

## Exporter runtime

The exporter runs inside Autodesk software; its runtime constraints are recorded in [ADR-011](adr/ADR-011-exporter-runtime.md). In summary:

- Civil 3D 2025 and 2026 use .NET 8, Civil 3D 2027 uses .NET 10, and 2024 and earlier use .NET Framework 4.8. The exporter multi-targets the runtimes it supports.
- Each drawing is extracted from its own database, because Civil 3D objects inside an Xref cannot be queried through the host drawing.
- Batch export without the full user interface may use the AutoCAD Core Console with Civil 3D loaded (`accoreconsole.exe /product C3D`).
- Which Civil 3D APIs behave correctly on side databases versus open documents must be confirmed on a representative project before discovery and extraction designs are finalized.

## Separation of responsibilities

| Component | Responsible for | Does not do |
| --- | --- | --- |
| Civil 3D exporter | Read Autodesk data; discover Xrefs, data shortcuts, and sheet sets; extract native objects, layouts, viewport layer states, styles, Xref lineage, transforms, and geometry; create `.corex` | QC or unnecessary engineering interpretation |
| CORE importer | Read `.corex`; create SourceEntities; normalize properties and units; create DesignObjects; build the Document Model; resolve relationships; validate; create `.core` | Re-extract Civil 3D data |
| CORE QC engine | Run after the canonical model exists and produce findings in the review store | Change source extraction or canonical-model creation |

```text
Civil 3D -> CORE Exporter -> .corex -> CORE Importer -> .core -> QC Engine -> Findings
```

This boundary is a foundational CORE architecture principle.
