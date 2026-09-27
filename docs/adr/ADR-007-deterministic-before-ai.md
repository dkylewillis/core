# ADR-007: Deterministic Checks Before AI

- Status: Accepted

For canonical-model creation, DesignObjects are created first by deterministic native Civil 3D mapping, then by explicit rule-based inference, with AI inference only as a fallback for ambiguity. AI-created mappings retain confidence and provenance.

For QC, deterministic checks run before AI-assisted interpretation. AI may summarize, classify, prioritize, or suggest follow-up questions using available evidence, but it must not overwrite or conceal deterministic results.
