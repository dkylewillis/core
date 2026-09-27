#!/usr/bin/env bash
# Validates schemas, fixture packages, and profiles, and checks that the SQLite schemas load.
# Requires: check-jsonschema (pip install check-jsonschema), sqlite3 3.37+.
set -euo pipefail
cd "$(dirname "$0")/.."

S=schemas/corex

echo "== metaschema"
check-jsonschema --check-metaschema schemas/corex/*.json schemas/profiles/*.json

for pkg in fixtures/corex/*/package; do
  echo "== $pkg"
  check-jsonschema --schemafile "$S/manifest.schema.json" "$pkg/manifest.json"
  check-jsonschema --schemafile "$S/drawing.schema.json" "$pkg"/drawings/*.json
  [ -f "$pkg/shortcuts/shortcuts.json" ] && check-jsonschema --schemafile "$S/shortcuts.schema.json" "$pkg/shortcuts/shortcuts.json"
  [ -f "$pkg/sheets/sheets.json" ] && check-jsonschema --schemafile "$S/sheets.schema.json" "$pkg/sheets/sheets.json"
done

echo "== profiles"
check-jsonschema --schemafile schemas/profiles/rule-profile.schema.json profiles/*.rule-profile.json
check-jsonschema --schemafile schemas/profiles/mapping-profile.schema.json profiles/*.mapping-profile.json

echo "== sqlite schemas"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
sqlite3 "$tmp/test.core" < schemas/core/core.sql
sqlite3 "$tmp/test.corereview" < schemas/core/corereview.sql

echo "all checks passed"
