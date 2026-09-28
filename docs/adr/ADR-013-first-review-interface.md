# ADR-013: Command-Line Tool and HTML Report as the First Review Interface

- Status: Accepted
- Date: 2026-09-27

## Context

The first build must prove the review loop: import, check, review findings with evidence, record dispositions, and carry them to a later snapshot. A full interactive application would consume most of the first build before the model and rules are trusted.

## Decision

The first review interface is the `core` command-line tool plus a generated, self-contained HTML report.

```text
core import   <package.corex | dir>  --out project.core [--no-ai]
core validate <project.core>
core check    <project.core> --review project.corereview --profile <profile.json> [--rule <id>...]
core findings <project.corereview> [--snapshot <id>] [--status open]
core dispose  <project.corereview> <finding-id> --decision accepted|dismissed|deferred|fixed --reason "..." [--reviewer <name>]
core carry    <project.corereview> --from <snapshot-id> --to <snapshot-id>
core report   <project.corereview> --snapshot <id> --out report.html
core diff     <old.core> <new.core>
```

The HTML report lists findings grouped by rule and sheet, and shows each finding's severity, basis, rule version, profile parameters, and evidence. Evidence includes source drawing, handle, layer, and coordinates, and for sheet-related findings the sheet, viewport, and visibility reason. The report is read-only; dispositions are recorded through `core dispose`.

Every command supports `--json` output so tests and later user interfaces can consume the same results.

## Consequences

- An interactive application (desktop or local web) is deferred until the first build's model and rules are trusted. It will read the same `.core` and `.corereview` files.
- Engineers can review findings without installing anything beyond the `core` tool.
