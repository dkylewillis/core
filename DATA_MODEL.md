# CORE Data Model

CORE data lives in two stores (see [ADR-009](docs/adr/ADR-009-identity-and-review-lifecycle.md)):

- **Canonical snapshot (`.core`)** — the validated, immutable model created from one `.corex` export.
- **Review store (`.corereview`)** — rule runs, observations, findings, evidence, and engineer dispositions across one or more snapshots.

## Canonical entities

### Project and sources

- **Project** — a bounded engineering effort and its source set. Its identity persists across snapshots.
- **Snapshot** — one validated canonical model of the project, with its `.corex` checksum, exporter and importer versions, creation time, and whether AI inference was enabled.
- **SourceFile** — an imported file plus immutable source metadata and checksum.
- **Drawing** — a DWG source within a project. Records `FingerprintGuid`, `VersionGuid`, units, coordinate system, and grid-to-ground scale.
- **XrefInstance** — an occurrence of a referenced drawing with explicit host drawing, referenced drawing, nesting parent, attachment type (attach or overlay), saved and resolved path, resolution status, clip boundary, and transform.
- **DataReference** — a Civil 3D data-shortcut link from a reference object in one drawing to its source object in another, with the shortcut file and resolution status. See [ADR-008](docs/adr/ADR-008-data-shortcuts.md).

### Source facts

- **SourceEntity** — a faithful, loss-minimized record of a native Civil 3D or AutoCAD entity, including its native identity, source geometry, properties, coordinate context, and extraction metadata. Unrecognized entities, such as proxy objects, are still SourceEntities.
- **Layer** — a drawing layer with its drawing-wide state (on/off, frozen, locked, plot) and, for Xref layers, the host override state.
- **BlockDefinition** — a block's contained entities. Entities inside a definition are SourceEntities owned by the definition.
- **BlockReference** — a SourceEntity that places a BlockDefinition with a transform and attribute values. Block references may be nested.
- **Style** — a Civil 3D object or label style.
- **DisplayComponent** — one component of a Style, with its view direction (plan, model, profile, section), visibility, and layer.
- **Geometry** — source or genuinely derived geometry with dimensional reference, coordinate context, transform lineage, precision, and derivation metadata. Analytic curves are preserved.

### Design Model

- **DesignObject** — a source-independent engineering object such as an alignment, profile, pipe, structure, pressure pipe, surface, parcel, or utility feature. It adds engineering meaning and relationships without unnecessarily duplicating SourceEntity geometry or native properties.
- **EntityMapping** — a provenance-bearing link between SourceEntities and DesignObjects. It supports direct mappings and many-to-many interpretations, and records its **method**: `native`, `rule`, or `ai`.
- **DesignRelationship** — an engineering relationship between DesignObjects, such as pipe-to-structure connection or profile-to-alignment.

### Document Model

- **Layout** — a paper-space layout in a drawing, with its paper-space entities and title block.
- **Viewport** — a view from a layout into model space, including scale, transform, clip boundary, on/off state, and per-viewport layer overrides. The layout's own paper-space viewport is excluded.
- **ProfileView** — a Civil 3D profile view's placement, station and elevation range, vertical exaggeration, and the alignment and profiles it shows. Section views follow the same pattern later.
- **Sheet** — a logical construction sheet: sheet number, title, and revision, taken from the sheet set or title block, pointing to the layout that produces it.
- **Document** — a non-design artifact such as a PDF set, report, calculation, specification, or standard.
- **SheetRendition** — a published page of a Sheet within a Document, such as one page of a PDF set.
- **Annotation** — a label, note, leader, dimension, or table as presented, with its displayed text, any parsed values (for example, `18" RCP`), whether its text is overridden, and the DesignObject it annotates when known.
- **PresentationInstance** — how a DesignObject is presented through a Viewport: the SourceEntity displayed, whether it is visible, and the reason when it is not (layer frozen, style component hidden, clipped, overlay not visible through parent, and so on).
- **CoverageIntent** — a Civil 3D view frame or match line that records which area a sheet is intended to cover.

## Review entities

- **Rule** — a versioned deterministic or AI-assisted review rule with declared inputs, applicability, units, and evidence requirements.
- **RuleProfile** — a named set of rule parameters for a project or jurisdiction (for example, minimum cover, minimum slope, water–sewer separation), with its source standard.
- **RuleRun** — one execution of a Rule against a Snapshot with a RuleProfile, recording the rule version, parameters, tolerances, snapshot checksum, and time.
- **Observation** — a measured or inferred fact produced by a RuleRun.
- **Finding** — a review issue with severity, status, rule reference, basis, and evidence. It may combine several observations.
- **Evidence** — source-linked support for an observation or finding. Each item references one target: a SourceEntity, DesignObject, Viewport, Layout, Sheet, Annotation, or a region of a Document.
- **Disposition** — an engineer's decision on a Finding (accepted, dismissed, deferred, fixed) with reason, reviewer, and time.

## Identity and lineage

Every imported entity has a stable CORE identifier and retains source identifiers, source file, extraction version, and lineage.

- A **Drawing** is identified by its `FingerprintGuid`, with the path as a secondary key. Copied drawings share a fingerprint, so a collision within one project is resolved by path and reported.
- A **SourceEntity** is identified by its drawing's identity plus its native handle. Handles are stable across saves but not across WBLOCK, copy and paste, or redrawing.
- A **DesignObject** is identified by the stable identities of the SourceEntities it maps to, plus its type.

A DesignObject may map to one or many SourceEntities, and a SourceEntity may support more than one DesignObject. EntityMapping records the relationship, creation method, confidence when applicable, and supporting evidence. No source relationship is discarded during normalization.

A data-shortcut reference and its source object map to the same DesignObject. The reference remains its own SourceEntity, linked through a DataReference.

Native mapping is preferred for Civil 3D objects. Rule-based inference is used when an explicit rule can interpret ordinary CAD entities. AI is a fallback only for ambiguity and must record its confidence and provenance; it never replaces the original SourceEntities.

## Basis

Every Observation and Finding records its **basis**: the least-certain mapping method among its inputs.

- `native` — every input came from native structured data.
- `rule` — at least one input came from rule-based inference.
- `ai` — at least one input came from AI inference.

A deterministic rule applied to AI-inferred inputs produces an `ai`-basis result, and is labeled as such. The basis is never upgraded by later processing.

## Minimum relationships

### Canonical snapshot

```text
Project 1--* Snapshot
Snapshot 1--* SourceFile
SourceFile 1--0..1 Drawing
Drawing 1--* Layer
Drawing 1--* SourceEntity
Drawing 1--* BlockDefinition
BlockDefinition 1--* SourceEntity
BlockReference *--1 BlockDefinition
Drawing 1--* XrefInstance            (as host)
XrefInstance *--1 Drawing            (referenced drawing)
XrefInstance 0..1--* XrefInstance    (nesting)
DataReference *--1 SourceEntity      (reference object)
DataReference *--1 SourceEntity      (source object)
Style 1--* DisplayComponent
DisplayComponent *--1 Layer
SourceEntity *--0..1 Style
SourceEntity *--* DesignObject       (through EntityMapping)
DesignObject *--* DesignObject       (through DesignRelationship)
Drawing 1--* Layout
Layout 1--* Viewport
Drawing 1--* ProfileView
Viewport *--* ProfileView            (profile views shown)
Layout 1--0..1 Sheet
Sheet 1--* CoverageIntent
Sheet 1--* SheetRendition
Document 1--* SheetRendition
Viewport 1--* PresentationInstance
DesignObject 1--* PresentationInstance
SourceEntity 1--* PresentationInstance
Annotation *--0..1 DesignObject
```

### Review store

```text
Rule 1--* RuleRun
RuleProfile 1--* RuleRun
Snapshot 1--* RuleRun
RuleRun 1--* Observation
Observation *--* Finding
Finding 1--* Evidence
Finding 1--* Disposition
Finding 0..1--* Finding              (carried forward to a later snapshot)
```

## Storage guidance

The `.core` file is a portable SQLite canonical snapshot. It holds relational metadata, source and mapping provenance, relationship and transform data, document presentation links, and geometry indexes. It is written once, when validation succeeds, and is not modified by review.

The `.corereview` file is a portable SQLite review store. It holds rule profiles, rule runs, observations, findings, evidence, and dispositions. It references snapshots by checksum and entities by stable identity, so it can span several snapshots of the same project.

Large or source-native payloads may remain in files referenced by checksum and path, with the database storing the manifest and extraction metadata. Coordinates follow [ADR-010](docs/adr/ADR-010-units-and-precision.md).
