# CORE Data Model

## Core entities

- **Project** — a bounded engineering effort and its source set.
- **SourceFile** — an imported file plus immutable source metadata and checksum.
- **Drawing** — a design source within a project, including model/layout context.
- **XrefInstance** — an occurrence of a referenced drawing with explicit parent, path, and transform.
- **DesignObject** — a canonical engineering object such as an alignment, pipe, surface, parcel, or utility feature.
- **Geometry** — source and normalized geometry with dimensional reference and coordinate context.
- **Document** — a non-design artifact such as a PDF, report, calculation, specification, or standard.
- **Sheet** — a published document or drawing sheet.
- **Viewport** — a view into model or sheet space, including scale and transform metadata.
- **Rule** — a versioned deterministic or AI-assisted review rule.
- **Observation** — a measured or inferred fact produced by processing.
- **Finding** — a review issue with severity, status, rule reference, and evidence.
- **Evidence** — source-linked support for an observation or finding.

## Identity and lineage

Every imported entity has a stable CORE identifier and retains source identifiers, source file, extraction version, and lineage. A canonical object may have multiple source representations, but no source relationship is discarded during normalization.

## Minimum relationships

```text
Project 1--* SourceFile
Project 1--* Drawing
Drawing 1--* XrefInstance
Drawing 1--* DesignObject
Project 1--* Document
Document 1--* Sheet
Sheet 1--* Viewport
Rule 1--* Observation 1--* Finding
Finding 1--* Evidence
```

## Storage guidance

SQLite should hold relational metadata, provenance, rule execution state, findings, and geometry indexes. Large or source-native payloads may remain in files referenced by checksum and path, with the database storing the manifest and extraction metadata.
