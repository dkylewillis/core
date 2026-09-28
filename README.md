# CORE

**CORE — Civil Object Review Engine** is a local-first engineering QA/QC system for analyzing civil design models, construction documents, calculations, and standards while preserving traceable evidence for every finding.

CORE is Civil 3D-first, but its canonical model is source-independent. The system prefers authoritative structured data, uses deterministic engineering checks whenever possible, and uses AI only where interpretation is necessary.

## Design baseline

- Civil 3D is the preferred structured source for the initial workflow.
- Source data is normalized into a canonical CORE model without losing source identity or lineage.
- Canonical-file creation is separate from QC: discover and extract the project, store SourceEntities, add DesignObjects and the Document Model, resolve relationships, validate, then save a portable SQLite `.core` file.
- Xrefs are represented as instances and transformed through an explicit coordinate pipeline; they are never silently flattened.
- Civil 3D data shortcuts are first-class references alongside Xrefs.
- Visibility on a sheet is resolved from layer state, Xref and block rules, and Civil 3D style display components, not from object location alone.
- Units are explicit (US survey feet and international feet are distinct) and coordinates are stored at 64-bit precision.
- Design objects and document artifacts are separate but linkable models.
- SQLite is the initial local storage layer. A `.core` file is an immutable canonical snapshot; findings and engineer dispositions live in a separate `.corereview` store that carries them across submittals.
- Every observation and finding carries provenance, evidence, and its basis (native data, rule inference, or AI inference).
- Deterministic checks run before AI-assisted interpretation.

## Documentation

- [Vision](VISION.md)
- [Architecture](ARCHITECTURE.md)
- [Data model](DATA_MODEL.md)
- [Coordinate and transform model](COORDINATES.md)
- [COREX Civil 3D exchange format](docs/COREX.md)
- [Initial QC rules](QC_RULES.md) and [rule specifications](docs/rules/README.md)
- [Architecture decision records](docs/adr/README.md)
- [Architecture diagrams](docs/diagrams/README.md)

## Building

- [First build plan](docs/BUILD_PLAN.md) — ordered tasks with acceptance criteria
- [Exporter spike](docs/EXPORTER_SPIKE.md) — Civil 3D investigation that precedes exporter work
- [Agent guide](AGENTS.md) — rules and commands for coding agents
- [COREX JSON Schemas](schemas/corex/), [SQLite schemas](schemas/core/), and [profiles](profiles/)
- [Fixtures](fixtures/corex/) — [mini-site](fixtures/corex/mini-site/) and [mini-site-rev2](fixtures/corex/mini-site-rev2/) with expected results

Validate schemas, fixtures, and profiles with `./scripts/validate.sh`.

The Civil 3D exporter plugin lives under [`exporter/`](exporter/README.md). It multi-targets `net8.0-windows` and `net10.0-windows` and compiles against Autodesk NuGet reference assemblies (runs only on Windows with Civil 3D).

## Scope of the first build

The first implementation should establish a reliable import, canonicalization, provenance, deterministic checking, and finding-review loop for a representative Civil 3D project. Cloud services, a complete civil ontology, autonomous design decisions, and custom AI models are intentionally out of scope.

The first build is a thin end-to-end slice:

1. **Exporter spike** — confirm extraction on one real project that uses nested Xrefs and data shortcuts, and settle the exporter runtime ([ADR-011](docs/adr/ADR-011-exporter-runtime.md)).
2. **One gravity storm network** with its plan and plan/profile sheets, exported to `.corex` and imported to `.core`.
3. **Three or four deterministic rules**: missing or invalid source reference, network connectivity, sheet/viewport coverage, and annotation vs. model.
4. **Review loop** — findings with evidence, engineer dispositions, and dispositions carried forward to a second snapshot of the same project.

AI inference is disabled for the first build.

## Status

This repository is a design baseline with the contracts and acceptance fixtures for the first build. Implementation starts with task T1 in the [build plan](docs/BUILD_PLAN.md) once [ADR-012](docs/adr/ADR-012-implementation-stack.md) and [ADR-013](docs/adr/ADR-013-first-review-interface.md) are approved.
