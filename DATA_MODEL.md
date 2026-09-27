# CORE Data Model

## Core entities

- **Project** — a bounded engineering effort and its source set.
- **SourceFile** — an imported file plus immutable source metadata and checksum.
- **Drawing** — a design source within a project, including model/layout context.
- **XrefInstance** — an occurrence of a referenced drawing with explicit parent, path, and transform.
- **SourceEntity** — a faithful, loss-minimized record of a native Civil 3D or AutoCAD entity, including its native identity, source geometry, properties, coordinate context, and extraction metadata.
- **DesignObject** — a source-independent engineering object such as an alignment, pipe, surface, parcel, or utility feature. It adds engineering meaning and relationships without unnecessarily duplicating SourceEntity geometry or native properties.
- **EntityMapping** — a provenance-bearing link between SourceEntities and DesignObjects. It supports direct mappings and many-to-many interpretations.
- **Geometry** — source or genuinely derived geometry with dimensional reference, coordinate context, transform lineage, and derivation metadata.
- **Document** — a non-design artifact such as a PDF, report, calculation, specification, or standard.
- **Sheet** — a published document or drawing sheet.
- **Viewport** — a view into model or sheet space, including scale and transform metadata.
- **PresentationInstance** — a link describing how a DesignObject is presented on a sheet through a layout, viewport, annotation, or other document element.
- **Rule** — a versioned deterministic or AI-assisted review rule.
- **Observation** — a measured or inferred fact produced by processing.
- **Finding** — a review issue with severity, status, rule reference, and evidence.
- **Evidence** — source-linked support for an observation or finding.

## Identity and lineage

Every imported entity has a stable CORE identifier and retains source identifiers, source file, extraction version, and lineage. A DesignObject may map to one or many SourceEntities, and a SourceEntity may support more than one DesignObject. EntityMapping records the relationship, creation method, confidence when applicable, and supporting evidence. No source relationship is discarded during normalization.

Native mapping is preferred for Civil 3D objects. Rule-based inference is used when an explicit rule can interpret ordinary CAD entities. AI is a fallback only for ambiguity and must record its confidence and provenance; it never replaces the original SourceEntities.

## Minimum relationships

```text
Project 1--* SourceFile
Project 1--* Drawing
Drawing 1--* XrefInstance
Drawing 1--* SourceEntity
SourceEntity *--* DesignObject (through EntityMapping)
Project 1--* Document
Document 1--* Sheet
Sheet 1--* Viewport
DesignObject *--* PresentationInstance
Viewport 1--* PresentationInstance
Rule 1--* Observation 1--* Finding
Finding 1--* Evidence
```

## Storage guidance

The `.core` file is a portable SQLite project store. It holds relational metadata, source and mapping provenance, relationship and transform data, document presentation links, geometry indexes, rule execution state, and findings. Large or source-native payloads may remain in files referenced by checksum and path, with the database storing the manifest and extraction metadata.
