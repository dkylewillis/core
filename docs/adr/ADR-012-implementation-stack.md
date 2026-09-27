# ADR-012: Implementation Stack

- Status: Proposed — awaiting owner approval
- Date: 2026-09-27

## Context

The exporter must be written in C# ([ADR-011](ADR-011-exporter-runtime.md)). The importer, QC engine, and review store have no Autodesk dependency and should be buildable and testable on Linux by cloud agents. The COREX contract must be shared by both sides without drift.

## Decision

| Concern | Choice |
| --- | --- |
| Language | C# for every component |
| Engine runtime | .NET 10 (LTS, supported to November 2028) |
| COREX contract library | `Core.Corex`, targeting `netstandard2.0` so the exporter (.NET 8 and .NET 10) and the engine share one set of types |
| Contract source of truth | The JSON Schemas in [`schemas/corex/`](../../schemas/corex/). C# types follow the schemas; tests validate every fixture and every exporter output against them |
| JSON | `System.Text.Json` |
| JSON Schema validation in tests | `JsonSchema.Net` |
| SQLite | `Microsoft.Data.Sqlite`, with the R*Tree module for coarse spatial indexing |
| 2D geometry | `NetTopologySuite`, used on exact 64-bit coordinates |
| Tests | xUnit, with fixture-driven tests against [`fixtures/`](../../fixtures/) |
| CLI | `System.CommandLine` |
| `.corex` package | A zip archive (deflate) of the directory layout in [COREX.md](../COREX.md#package-structure). Readers also accept the unpacked directory, which is how fixtures are stored |
| Binary geometry | The `CXB1` format defined in [COREX.md](../COREX.md#binary-geometry-cxb1) |

### Solution layout

```text
src/
  Core.Corex/            COREX contract types, reader, writer (netstandard2.0)
  Core.Model/            canonical model types
  Core.Import/           .corex -> .core pipeline
  Core.Store/            .core and .corereview SQLite access and migrations
  Core.Rules/            rule engine and the initial rules
  Core.Report/           HTML findings report
  Core.Cli/              `core` command-line tool
exporter/
  Core.Exporter.Civil3D/ Civil 3D exporter (net8.0-windows; net10.0-windows)
tests/
  Core.*.Tests/
```

## Alternatives considered

- **Python for the engine.** Faster to prototype and has strong geometry libraries, but it means two toolchains, no shared contract types with the exporter, and the schema becomes the only guard against drift.
- **Rust for the engine.** Strong correctness and performance, but no shared types with the exporter and a smaller civil/CAD ecosystem.

## Consequences

- Cloud agents need only the .NET 10 SDK to build and test everything except the exporter.
- The exporter compiles on any machine with the NuGet reference assemblies but runs only on Windows with Civil 3D.
- A schema change requires updating the schema, the `Core.Corex` types, and the fixtures together.
