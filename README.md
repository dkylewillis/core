# CORE

**CORE — Civil Object Review Engine** is a local-first engineering QA/QC system for analyzing civil design models, construction documents, calculations, and standards while preserving traceable evidence for every finding.

CORE is Civil 3D-first, but its canonical model is source-independent. The system prefers authoritative structured data, uses deterministic engineering checks whenever possible, and uses AI only where interpretation is necessary.

## Design baseline

- Civil 3D is the preferred structured source for the initial workflow.
- Source data is normalized into a canonical CORE model without losing source identity or lineage.
- Canonical-file creation is separate from QC: discover and extract the project, store SourceEntities, add DesignObjects and the Document Model, resolve relationships, validate, then save a portable SQLite `.core` file.
- Xrefs are represented as instances and transformed through an explicit coordinate pipeline; they are never silently flattened.
- Design objects and document artifacts are separate but linkable models.
- SQLite is the initial local storage layer.
- Every observation and finding carries provenance and evidence.
- Deterministic checks run before AI-assisted interpretation.

## Documentation

- [Vision](VISION.md)
- [Architecture](ARCHITECTURE.md)
- [Data model](DATA_MODEL.md)
- [Coordinate and transform model](COORDINATES.md)
- [Initial QC rules](QC_RULES.md)
- [Agent contribution rules](AGENTS.md)
- [Architecture decision records](docs/adr/README.md)
- [Architecture diagrams](docs/diagrams/README.md)

## Scope of the first build

The first implementation should establish a reliable import, canonicalization, provenance, deterministic checking, and finding-review loop for a representative Civil 3D project. Cloud services, a complete civil ontology, autonomous design decisions, and custom AI models are intentionally out of scope.

## Status

This repository is a design baseline. The documentation is intended to make foundational decisions explicit before implementation begins.
