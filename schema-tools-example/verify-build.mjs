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
// 1. Generated SQL files - single _generated directory
// ---------------------------------------------------------------------------
console.log("\n--- Generated SQL ---");

const generatedDir = join(root, "Schema", "_generated");
const analysisPath = join(root, "analysis.json");
assert(existsSync(generatedDir), "Schema/_generated/ directory exists");

const expectedFiles = [
  // Triggers (12)
  "trg_countries_cascade_soft_delete.sql",
  "trg_country_dialling_codes_reactivation_guard.sql",
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
  // Views (9)
  "vw_comments.sql",
  "vw_countries.sql",
  "vw_country_dialling_codes.sql",
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
// 2. Column naming - generated SQL should use record_ prefixed columns
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
// 3. Post-build metadata - Build/schema.json
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
// 4. Documentation - Docs/SCHEMA.md
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
// 5. Primary Key correctness - non-id and composite PK tables
// ---------------------------------------------------------------------------
console.log("\n--- PK Correctness (ALTER TABLE constraints) ---");

// 5a. Purge procedure must reference iso_alpha2 for countries, not id
const purgeFile = join(generatedDir, "usp_purge_soft_deleted.sql");
if (existsSync(purgeFile)) {
  const purge = readFileSync(purgeFile, "utf8");

  // countries uses iso_alpha2 PK
  assert(
    purge.includes("t.iso_alpha2 = h.iso_alpha2"),
    "Purge proc joins countries on iso_alpha2 (not id)"
  );
  assert(
    !purge.match(/\bcountries\b[\s\S]{0,200}\bt\.id\b/),
    "Purge proc does NOT reference t.id for countries"
  );

  // country_dialling_codes uses composite PK
  assert(
    purge.includes("t.country_code = h.country_code")
      && purge.includes("t.dialling_code = h.dialling_code"),
    "Purge proc joins country_dialling_codes on composite PK"
  );
}

// 5b. Cascade trigger for countries must use iso_alpha2 in PK join
const countriesTrigger = join(
  generatedDir,
  "trg_countries_cascade_soft_delete.sql"
);
if (existsSync(countriesTrigger)) {
  const content = readFileSync(countriesTrigger, "utf8");
  assert(
    content.includes("i.iso_alpha2 = d.iso_alpha2"),
    "Countries cascade trigger joins on iso_alpha2"
  );
  assert(
    !content.includes("i.id = d.id"),
    "Countries cascade trigger does NOT use id"
  );
}

// 5c. Reactivation guard for country_dialling_codes must use composite PK
const diallingGuard = join(
  generatedDir,
  "trg_country_dialling_codes_reactivation_guard.sql"
);
if (existsSync(diallingGuard)) {
  const content = readFileSync(diallingGuard, "utf8");
  assert(
    content.includes("i.country_code = d.country_code")
      && content.includes("i.dialling_code = d.dialling_code"),
    "Dialling codes guard trigger joins on composite PK"
  );
  assert(
    !content.includes("i.id = d.id"),
    "Dialling codes guard trigger does NOT use id"
  );
}

// 5d. analysis.json should have correct PKs for the new tables
if (existsSync(analysisPath)) {
  const raw = readFileSync(analysisPath, "utf8").replace(/^\uFEFF/, "");
  const analysis = JSON.parse(raw);
  const countriesEntry = analysis.tables.find((t) => t.name === "countries");
  const diallingEntry = analysis.tables.find(
    (t) => t.name === "country_dialling_codes"
  );

  assert(
    countriesEntry != null,
    "analysis.json contains countries table"
  );
  if (countriesEntry) {
    assert(
      JSON.stringify(countriesEntry.primaryKeyColumns) ===
        JSON.stringify(["iso_alpha2"]),
      `countries PK = [iso_alpha2] (got ${JSON.stringify(countriesEntry.primaryKeyColumns)})`
    );
  }

  assert(
    diallingEntry != null,
    "analysis.json contains country_dialling_codes table"
  );
  if (diallingEntry) {
    assert(
      JSON.stringify(diallingEntry.primaryKeyColumns) ===
        JSON.stringify(["country_code", "dialling_code"]),
      `country_dialling_codes PK = [country_code, dialling_code] (got ${JSON.stringify(diallingEntry.primaryKeyColumns)})`
    );
  }
}

// ---------------------------------------------------------------------------
// 6. Stale directories should not exist
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
