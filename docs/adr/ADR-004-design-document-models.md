# ADR-004: Separate Design and Document Models

- Status: Accepted
- Date: 2026-09-25

## Decision

Structured design entities and document artifacts are separate models connected by explicit links.

## Rationale

Their identity, geometry, extraction methods, and lifecycle differ. Combining them would make both less precise.

## Consequences

Sheets, viewports, PDFs, calculations, and standards can be reviewed alongside design objects without pretending they are the same type of thing.
