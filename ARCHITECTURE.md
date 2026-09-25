# CORE Architecture

## Boundaries

CORE is organized around five boundaries:

1. **Source adapters** read Civil 3D and, later, other structured or document sources.
2. **Canonical model** represents design objects, relationships, documents, and provenance independently of any one source format.
3. **Processing pipeline** resolves identity, Xref lineage, coordinates, geometry, and derived relationships.
4. **Review engine** runs deterministic checks first, then optional AI-assisted interpretation.
5. **Evidence and review** stores findings, supporting evidence, status, and engineer disposition.

## Preferred flow

```text
Source files -> adapter -> canonical model -> coordinate/relationship resolution
             -> deterministic checks -> optional AI interpretation -> findings/evidence
```

The source remains authoritative for source facts. CORE-derived facts must be labeled as derived and traceable to their inputs.

## Local-first deployment

The initial runtime is a local application backed by SQLite. Files remain under the user's control. External services may be added later behind explicit adapters, but they must not be prerequisites for inspecting an imported project or its findings.

## Design and Document models

The Design model captures structured engineering entities and their relationships. The Document model captures sheets, layouts, viewports, PDFs, calculations, specifications, and other review artifacts. They are separate because their identities, geometry, and lifecycle differ; cross-links connect them where evidence or review requires it.

See the [diagram set](docs/diagrams/README.md) and [ADRs](docs/adr/README.md) for the decisions behind this structure.
