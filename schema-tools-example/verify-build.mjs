#!/usr/bin/env node
/**
 * Verifies that SchemaTools generated the expected outputs after building
 * the example project. Run from the schema-tools-example directory.
 *
 * Usage: node verify-build.mjs
 */

import { readFileSync, existsSync, readdirSync } from "node:fs";
import { join, resolve } from "node:path";

const root = resolve(import.meta.dirname);
let failures = 0;

function assert(condition, message) {
  if (!condition) {
    console.error(`  FAIL: ${message}`);
    failures++;
  } else {
    console.log(`  OK:   ${message}`);
  }
}

// ---------------------------------------------------------------------------
// 1. Generated SQL files — single _generated directory
// ---------------------------------------------------------------------------
console.log("\n--- Generated SQL ---");

const generatedDir = join(root, "Schema", "_generated");
assert(existsSync(generatedDir), "Schema/_generated/ directory exists");

const expectedFiles = [
  // Triggers (9)
  "trg_orders_cascade_soft_delete.sql",
  "trg_orders_reactivation_guard.sql",
  "trg_order_items_reactivation_guard.sql",
  "trg_organisations_restrict_soft_delete.sql",
  "trg_organisation_members_cascade_soft_delete.sql",
  "trg_organisation_members_reactivation_guard.sql",
  "trg_users_cascade_reactivation.sql",
  "trg_users_cascade_soft_delete.sql",
  "trg_user_profiles_reactivation_guard.sql",
  // Procedure (1)
  "usp_purge_soft_deleted.sql",
  // Views (7)
  "vw_comments.sql",
  "vw_orders.sql",
  "vw_order_items.sql",
  "vw_organisations.sql",
  "vw_organisation_members.sql",
  "vw_users.sql",
  "vw_user_profiles.sql",
];

if (existsSync(generatedDir)) {
  const actual = readdirSync(generatedDir).sort();
  for (const file of expectedFiles) {
    assert(actual.includes(file), `${file} present`);
  }
  assert(
    actual.length === expectedFiles.length,
    `Expected ${expectedFiles.length} files, found ${actual.length}`
  );
}

// ---------------------------------------------------------------------------
// 2. Column naming — generated SQL should use record_ prefixed columns
// ---------------------------------------------------------------------------
console.log("\n--- Column Naming ---");

const triggerFile = join(
  generatedDir,
  "trg_users_cascade_soft_delete.sql"
);
if (existsSync(triggerFile)) {
  const content = readFileSync(triggerFile, "utf8");
  assert(content.includes("record_active"), "Trigger references record_active");
  assert(
    content.includes("record_updated_by"),
    "Trigger references record_updated_by"
  );
}

// ---------------------------------------------------------------------------
// 3. Post-build metadata — Build/schema.json
// ---------------------------------------------------------------------------
console.log("\n--- Post-Build Metadata ---");

const metadataPath = join(root, "Build", "schema.json");
assert(existsSync(metadataPath), "Build/schema.json exists");

if (existsSync(metadataPath)) {
  const raw = readFileSync(metadataPath, "utf8").replace(/^\uFEFF/, "");
  const metadata = JSON.parse(raw);
  assert(metadata.$schema === "./schema.schema.json", "$schema value correct");
  assert(Array.isArray(metadata.tables), "tables array present");
  assert(metadata.tables.length > 0, "At least one table in metadata");
  assert(metadata.statistics != null, "statistics object present");
}

// ---------------------------------------------------------------------------
// 4. Documentation — Docs/SCHEMA.md
// ---------------------------------------------------------------------------
console.log("\n--- Documentation ---");

const docsPath = join(root, "Docs", "SCHEMA.md");
assert(existsSync(docsPath), "Docs/SCHEMA.md exists");

if (existsSync(docsPath)) {
  const content = readFileSync(docsPath, "utf8");
  assert(content.length > 500, "SCHEMA.md has substantial content");
  assert(content.includes("## "), "SCHEMA.md has Markdown headings");
}

// ---------------------------------------------------------------------------
// 5. Stale directories should not exist
// ---------------------------------------------------------------------------
console.log("\n--- No Stale Directories ---");

const staleDirs = [
  join(root, "Schema", "Triggers", "_generated"),
  join(root, "Schema", "Views", "_generated"),
  join(root, "Schema", "Procedures", "_generated"),
];
for (const dir of staleDirs) {
  const shortName = dir.replace(root, "").replace(/\\/g, "/");
  assert(!existsSync(dir), `${shortName} does not exist (consolidated)`);
}

// ---------------------------------------------------------------------------
// Summary
// ---------------------------------------------------------------------------
console.log("\n" + "=".repeat(60));
if (failures > 0) {
  console.error(`FAILED: ${failures} check(s) failed`);
  process.exit(1);
} else {
  console.log("ALL CHECKS PASSED");
}
