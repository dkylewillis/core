# ADR-005: SQLite as Initial Storage

- Status: Accepted
- Date: 2026-09-25

SQLite is the initial local storage layer because it supports relational metadata, transactions, indexes, reproducible workflows, and low operational burden. A validated project is saved as a portable `.core` SQLite file. The schema must be versioned; large source payloads may remain in files referenced by manifests and checksums.

Amended 2026-09-27 by [ADR-009](ADR-009-identity-and-review-lifecycle.md): the `.core` file is an immutable canonical snapshot, and review data is kept in a separate SQLite review store (`.corereview`).
