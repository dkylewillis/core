# SQLite Schemas

- [`core.sql`](core.sql) — the canonical snapshot (`.core`).
- [`corereview.sql`](corereview.sql) — the review store (`.corereview`).

Both implement [DATA_MODEL.md](../../DATA_MODEL.md). Once the first build writes real files, these become migration `0001`; later changes are new numbered migrations, and the `meta.schema_version` value is incremented. A `.core` file is never migrated in place: it is an immutable snapshot, so a reader either supports its schema version or reports that it cannot open it. A `.corereview` file is migrated in place.

Both files load with the `sqlite3` CLI (3.37 or later):

```bash
sqlite3 test.core < schemas/core/core.sql
sqlite3 test.corereview < schemas/core/corereview.sql
```
