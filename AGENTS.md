# Agent Guide

Instructions for coding agents working in this repository. Read [README.md](README.md) first, then the document for the area you are changing.

## Where things are

| Path | Contents |
| --- | --- |
| `VISION.md`, `ARCHITECTURE.md`, `DATA_MODEL.md`, `COORDINATES.md`, `QC_RULES.md` | Design baseline |
| `docs/adr/` | Architecture decision records |
| `docs/COREX.md` | Civil 3D exchange format (intent) |
| `docs/rules/` | Normative rule specifications and visibility resolution |
| `docs/BUILD_PLAN.md` | Ordered first-build tasks with acceptance criteria |
| `docs/EXPORTER_SPIKE.md` | Civil 3D investigation that precedes exporter work |
| `schemas/corex/` | COREX JSON Schemas (normative) |
| `schemas/profiles/` | Rule and mapping profile schemas |
| `schemas/core/` | SQLite schemas for `.core` and `.corereview` |
| `profiles/` | Default rule and mapping profiles |
| `fixtures/corex/` | COREX fixture packages with expected results |
| `src/`, `tests/`, `exporter/` | Code, per [ADR-012](docs/adr/ADR-012-implementation-stack.md) (created by build task T1) |

## Commands

```bash
# Validate fixtures and profiles against their schemas (also run in CI)
./scripts/validate.sh

# After T1
dotnet build
dotnet test
```

## Rules

1. **ADRs are binding.** Do not change an accepted ADR's decision without the owner's approval. If implementation shows a decision is wrong, stop and report it, or propose a new ADR marked Proposed.
2. **Schemas are the contract.** `schemas/corex/` defines COREX. A change to a schema updates, in the same pull request, the `Core.Corex` types, every affected fixture, and `docs/COREX.md`.
3. **Fixtures are the acceptance tests.** Do not edit `fixtures/**/expected/` to make a failing test pass. Change expected results only when the spec changes, and say so in the pull request.
4. **Specs before behavior.** A rule change updates its spec in `docs/rules/` and increments the rule version.
5. **No finding without evidence.** A rule that cannot attach the evidence its spec requires emits nothing, or an observation if the spec says so.
6. **Deterministic by default.** Nothing in the first build calls an AI service. Imports run with AI disabled.
7. **Precision and units.** Coordinates are 64-bit. Never assume feet: read units from the drawing. US survey feet and international feet are different units.
8. **Snapshots are immutable.** Only the importer writes `.core` files. Review data goes to `.corereview`.
9. **Keep docs current.** If code changes behavior described in a document, update the document in the same pull request.
10. **Build tasks.** When working a task from `docs/BUILD_PLAN.md`, meet every acceptance criterion and reference the task id (for example `T6`) in the pull request title.

## Civil 3D and the exporter

Cloud agents cannot run Civil 3D. The exporter can be compiled against the Autodesk NuGet reference assemblies but can only be run and tested on Windows with Civil 3D installed. Everything downstream of `.corex` must be developed and tested against fixtures.

Never commit client drawings or `.corex` packages exported from client projects.
