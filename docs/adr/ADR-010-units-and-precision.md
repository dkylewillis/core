# ADR-010: Explicit Units and 64-Bit Coordinate Precision

- Status: Accepted
- Date: 2026-09-27

## Context

Civil projects commonly use state-plane coordinates in the millions of feet. At that magnitude a 32-bit float resolves only to roughly a quarter foot, and the difference between US survey feet and international feet amounts to several feet. Both errors are silent.

## Decision

- Every drawing records its linear units, including the US survey foot versus international foot distinction, its coordinate system code when set, and any grid-to-ground scale factor.
- Unit and coordinate reference conversions are explicit, recorded operations.
- Exact coordinates are stored as 64-bit floats or relative to a recorded local origin, in COREX and in `.core`.
- Approximate spatial indexes (such as SQLite R*Tree, which uses 32-bit floats by default) may be used only for candidate selection; checks re-test against exact geometry.
- Analytic curves are preserved; any tessellation records its tolerance.

## Consequences

The binary geometry format chosen for COREX must support 64-bit coordinates or a local origin. Findings that depend on distances report the units and tolerance used.
