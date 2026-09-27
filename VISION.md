# CORE Vision

## Product definition

CORE (Civil Object Review Engine) is a local-first engineering QA/QC system that analyzes civil design models, construction documents, calculations, and standards to identify inconsistencies and potential design issues while maintaining traceable evidence for every finding.

## Primary user and workflow

The initial user is a civil/site-development design engineer working primarily in AutoCAD Civil 3D. CORE should help that engineer understand a project as a connected system, run repeatable checks, and review findings without losing the connection to the source drawing, object, document, or rule that produced them.

## Principles

1. Prefer authoritative structured engineering data whenever available.
2. Use deterministic engineering checks whenever a rule can be expressed deterministically.
3. Use AI for interpretation, classification, and assistance—not as an unsupported source of truth.
4. Never present an unsupported finding.
5. Preserve source provenance, coordinate context, and Xref lineage.
6. Keep the first workflow local, inspectable, and reproducible.

## Initial success criteria

Given a representative Civil 3D project, CORE can import source data, build a canonical project model, preserve drawing, Xref, and data-shortcut relationships, run initial deterministic checks, and present findings with enough evidence for an engineer to verify or dismiss them.

When the same project is exported again after revisions, CORE carries the engineer's dispositions forward to matching findings and shows what changed between the two snapshots.

## Non-goals for the design baseline

Cloud collaboration, a complete civil engineering ontology, automated design modification, and custom model training are deferred until the local evidence and checking loop is trustworthy.
