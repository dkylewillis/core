# First Build Plan

The first build is the thin end-to-end slice described in the [README](../README.md#scope-of-the-first-build). Tasks are ordered; each is sized for one agent task and has acceptance criteria that can be checked by tests. The engine tasks (T1–T16) run on Linux and never need Civil 3D. The exporter (T17) needs Windows with Civil 3D and follows the [exporter spike](EXPORTER_SPIKE.md).

Before T1, the owner approves or amends [ADR-012](adr/ADR-012-implementation-stack.md), [ADR-013](adr/ADR-013-first-review-interface.md), and the [SQLite schema drafts](../schemas/core/).

## Task list

| Task | Depends on | Summary |
| --- | --- | --- |
| T1 | — | Solution scaffold and CI |
| T2 | T1 | COREX contract library |
| T3 | T1 | SQLite stores |
| T4 | T2, T3 | Import: source facts |
| T5 | T4 | Import: transforms and geometry |
| T6 | T4 | Import: DesignObjects |
| T7 | T5 | Import: Document Model |
| T8 | T6, T7 | Import: presentation and visibility |
| T9 | T8 | Import: validation and `core import` |
| T10 | T3, T9 | Rule engine and review store |
| T11 | T10 | Rule `core.source-reference` |
| T12 | T10 | Rule `core.network-connectivity` |
| T13 | T10 | Rule `core.sheet-coverage` |
| T14 | T10 | Rule `core.annotation-vs-model` |
| T15 | T11–T14 | Dispositions, carry-forward, and diff |
| T16 | T15 | HTML report |
| T17 | spike | Civil 3D exporter |

T5 and T6 can run in parallel; T11–T14 can run in parallel.

## Tasks

### T1 — Solution scaffold and CI

Create the solution layout in ADR-012 with empty projects and one passing test per test project. Extend CI to run `dotnet build` and `dotnet test` alongside the existing schema checks.

Acceptance:
- `dotnet build` and `dotnet test` succeed from a clean clone with only the .NET 10 SDK installed.
- CI runs both, plus the existing fixture validation.
- `Core.Corex` targets `netstandard2.0`; every other project targets `net10.0`.

### T2 — COREX contract library (`Core.Corex`)

C# types for every schema in `schemas/corex/`, a reader for both zip packages and unpacked directories, a writer, and JSON Schema validation.

Acceptance:
- Reads both fixture packages without loss: read, write, and read again produces equal objects.
- Validates each package file against its schema and reports the file and JSON path of every error.
- Rejects an invalid package: add `fixtures/corex/invalid/` cases (missing typed payload, unknown field, bad unit, paper entity without owner, non-hex handle) with tests.
- Reads the same package zipped and unzipped with identical results.

### T3 — SQLite stores (`Core.Store`)

Create `.core` and `.corereview` files from the approved schemas, with a migration runner and `meta.schema_version`.

Acceptance:
- Creating either store yields exactly the tables in `schemas/core/`.
- A `.core` file is opened read-only by everything except the importer.
- Opening a store with an unsupported schema version fails with a clear message.

### T4 — Import: source facts

Create Snapshot, SourceFiles, Drawings, Layers, BlockDefinitions, Styles and DisplayComponents, SourceEntities (with payloads), XrefInstances, DataReferences, and Shortcuts. Normalize units per [ADR-010](adr/ADR-010-units-and-precision.md).

Acceptance (mini-site):
- `expected/import.json` → `drawings` and `dataReferences` match.
- Every SourceEntity in the package appears once with its drawing and handle.

### T5 — Import: transforms and geometry

Resolve Xref paths from every drawing, compose world transforms (Xref and block chains, unit scaling, OCS), store geometry at 64-bit precision with the R*Tree coarse index, and implement profile-view mapping.

Acceptance (mini-site):
- `xrefPaths` match exactly, including `loaded` and `reason`.
- `geometryChecks.worldPoints` and `profileViewPoints` match within `tolerance`.

### T6 — Import: DesignObjects

Native mapping for gravity networks, pipes, structures, alignments, profiles, and surfaces; merge data-shortcut references into their source DesignObjects; classify networks by system with the mapping profile; compute stable keys and normalized properties.

Acceptance (mini-site):
- `designObjects` match exactly: types, names, systems, mappings (with `method` and `via`), and properties.
- Classification records method `rule` and the mapping profile entry id.

### T7 — Import: Document Model

Layouts, viewports (kind and world footprint), sheets from `sheets.json` and title blocks, annotations (plain text, parsed callouts, native label links), and coverage intents.

Acceptance (mini-site):
- `geometryChecks.viewportFootprints` match.
- C5.01's viewport is classified `profile` and C3.01's `plan`.

### T8 — Import: presentation and visibility

PresentationInstances per [visibility.md](rules/visibility.md).

Acceptance (mini-site):
- Every entry in `expected/presentation.json` matches, including ordered `reasons` and `effectiveLayer`.

### T9 — Import: validation and `core import`

Validation per [ARCHITECTURE.md](../ARCHITECTURE.md#7-validate-and-save); `core import` and `core validate` per ADR-013.

Acceptance (mini-site):
- `validation` matches: no errors; the listed warnings.
- `core import fixtures/corex/mini-site/package --out x.core --no-ai` exits 0 and writes a `.core`; with `--json` it prints the snapshot id and warnings.
- An import with a validation error writes no `.core` and exits non-zero.

### T10 — Rule engine and review store

Rule registry, profile loading and schema validation, rule runs, observations, findings, and evidence; basis computation; `core check` and `core findings`.

Acceptance:
- A run with no rules enabled records a rule run per enabled rule (none) and succeeds.
- Each rule run records the rule version, profile SHA-256, parameters, and snapshot id.
- Rerunning the same rules and profile on the same snapshot yields identical findings.

### T11–T14 — The four rules

Implement each rule to its spec in [docs/rules/](rules/README.md).

Acceptance (per rule, mini-site and mini-site-rev2):
- The rule's findings and observations in `expected/findings.json` match exactly, including `severity`, `basis`, and `facts`.
- No entry in `mustNotReport` for that rule is reported.
- Unit tests cover each output code, including codes the fixtures do not exercise (for example `connection-mismatch`, `callout-ambiguous`).

### T15 — Dispositions, carry-forward, and diff

`core dispose`, `core carry`, and `core diff` per ADR-009 and ADR-013.

Acceptance: the procedure in [mini-site-rev2](../fixtures/corex/mini-site-rev2/README.md) produces `expected/findings.json` and `expected/carry.json`, including the fallback match for P-4 and both resolved-by-change findings.

### T16 — HTML report

`core report` per ADR-013.

Acceptance:
- A single self-contained HTML file with no external requests.
- Every finding appears with severity, basis, rule version, profile parameters, status, and evidence.
- Snapshot test of the report for mini-site.

### T17 — Civil 3D exporter

After the [exporter spike](EXPORTER_SPIKE.md) is complete and ADR-011 is accepted. Built and tested on Windows with Civil 3D.

Acceptance:
- Output for a representative project validates against `schemas/corex/`.
- A hand-built Civil 3D reproduction of mini-site exports to a package that imports with the same `expected/` results (handles and GUIDs excepted).
