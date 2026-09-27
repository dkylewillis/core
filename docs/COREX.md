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

The exporter identifies the drawings that make up the project, including sheet, design, base, survey, Xref, and nested-Xref drawings. It preserves the dependency structure rather than flattening it.

```text
C301.dwg
|
|-- C-BASE.dwg
|   `-- SURVEY.dwg
|
|-- C-GRAD.dwg
|-- C-STRM.dwg
`-- C-UTIL.dwg
```

### 2. Drawing extraction

For every drawing, COREX records the AutoCAD information needed by CORE, including drawing metadata, layers, blocks, lines, polylines, 3D polylines, text, MText, MLeaders, dimensions, tables, hatches, and Xrefs. Native handles and identifiers are preserved whenever possible.

### 3. Civil 3D extraction

COREX also records native Civil 3D objects. Initial support focuses on pipe networks, pipes, structures, surfaces, feature lines, alignments, profiles, and profile views. Additional object types can be added later.

The exporter preserves native Civil 3D properties rather than prematurely converting them into CORE engineering concepts.

## Sheets, layouts, and viewports

Layouts represent what is ultimately presented on construction drawings. COREX records each layout's paper-space entities, title block, notes, tables, and viewports.

For every viewport, COREX preserves:

- Paper-space position, dimensions, boundary, and nonrectangular clipping boundary when applicable.
- Model-space view center, target, direction, height, custom scale, twist/rotation, and other values required to reconstruct the viewport transform.

This allows CORE to relate model/world coordinates to sheet/paper coordinates and determine each viewport's world-coordinate footprint.

### Viewport layer state

Layer visibility is preserved for each viewport, including layer on/off state, global freeze state, viewport freeze state, and relevant viewport-specific overrides. For Xref objects, the applicable host and Xref layer state is also preserved.

This distinguishes an object located inside a viewport from one actually visible in that viewport.

```text
Design Object: DI-12
Source: C-STRM.dwg
Layer: C-STRM-STRC

    v Xref

Host layer: C-STRM|C-STRM-STRC

    v Viewport VP-1

VP frozen: true

DI-12 is not presented in this viewport.
```

## Xref lineage and transforms

COREX preserves the complete Xref hierarchy and transformation for every Xref instance.

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
Source geometry
  -> Xref transform chain
  -> World geometry
  -> Viewport transform
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
|-- sheets/
|   `-- sheets.json
|
`-- geometry/
    |-- EG.*
    |-- FG.*
    `-- ...
```

JSON is appropriate for metadata, object properties, relationships, layers, Xrefs, layouts, viewports, and Civil 3D attributes. Large geometry, such as TIN surfaces, dense meshes, and large point collections, should use an efficient binary representation referenced by JSON records. The particular binary format remains an implementation decision.

## COREX is not the canonical model

COREX represents what Civil 3D reported, not CORE's final interpretation of the project.

```text
COREX
Native type: AeccDbPipe
Handle: 2A7F
Name: P-12
Diameter: 2.0 drawing units

CORE
DesignObject: StormPipe
Name: P-12
Diameter: 24 in
```

The CORE importer reads `.corex`, creates SourceEntities, normalizes values and units, creates DesignObjects, builds the Document Model, resolves relationships, validates the result, and saves `project.core`.

DesignObject creation follows this priority:

1. Structured deterministic information
2. Rule-based interpretation
3. AI-assisted interpretation only when necessary

For example, a Civil 3D pipe can deterministically become a `StormPipe` DesignObject without AI.

## Debugging and development boundary

COREX is the explicit boundary between Civil 3D extraction and CORE normalization. If CORE reports `P-12` as 18 inches but `.corex` records 24 inches, the error is in import or normalization. If `.corex` records 18 inches, the error is in extraction. This makes both systems easier to test.

It also lets the CORE engine be developed and tested without Civil 3D running. A developer can receive `SmithFarm.corex` and test import, normalization, relationships, geometry, viewport mapping, QC rules, UI, and findings without receiving the original DWGs or requiring Autodesk software.

## Separation of responsibilities

| Component | Responsible for | Does not do |
| --- | --- | --- |
| Civil 3D exporter | Read Autodesk data; extract native objects, layouts, viewport layer states, Xref lineage, transforms, and geometry; create `.corex` | QC or unnecessary engineering interpretation |
| CORE importer | Read `.corex`; create SourceEntities; normalize properties and units; create DesignObjects; build the Document Model; resolve relationships; validate; create `.core` | Re-extract Civil 3D data |
| CORE QC engine | Run after the canonical model exists and produce findings | Change source extraction or canonical-model creation |

```text
Civil 3D -> CORE Exporter -> .corex -> CORE Importer -> .core -> QC Engine -> Findings
```

This boundary is a foundational CORE architecture principle.
